using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Kvx.Server;
using Kvx.Server.Names;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kvx.Tests;

/// <summary>The *.localhost responder Windows reaches through its NRPT rule; run here on a free port on any OS.</summary>
public sealed class LoopbackDnsTests
{
    private static byte[] Query(string name, ushort type = 1, ushort id = 0x1234)
    {
        var bytes = new List<byte>();
        bytes.AddRange([(byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0]);   // RD, one question
        foreach (var label in name.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }
        bytes.AddRange([0, (byte)(type >> 8), (byte)type, 0, 1]);
        return [.. bytes];
    }

    private static int Rcode(byte[] reply) => reply[3] & 0x0F;
    private static int Answers(byte[] reply) => BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(6));
    private static IPAddress Address(byte[] reply) => new(reply.AsSpan(reply.Length - 4));

    [Theory]
    [InlineData("secrets.dev.localhost")]
    [InlineData("New-Vault.KVXTEST.localhost")]
    [InlineData("localhost")]
    public void Names_under_localhost_are_loopback(string name)
    {
        var query = Query(name);
        var reply = LoopbackDns.Answer(query)!;

        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16BigEndian(reply));
        Assert.Equal(0, Rcode(reply));
        Assert.Equal(1, Answers(reply));
        Assert.Equal(IPAddress.Loopback, Address(reply));
    }

    [Fact]
    public void Aaaa_has_no_answer_so_clients_use_the_address_kvx_listens_on()
    {
        var reply = LoopbackDns.Answer(Query("secrets.dev.localhost", type: 28))!;
        Assert.Equal(0, Rcode(reply));
        Assert.Equal(0, Answers(reply));
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("notlocalhost")]
    [InlineData("localhost.example.com")]
    public void Other_names_are_refused(string name)
    {
        var reply = LoopbackDns.Answer(Query(name))!;
        Assert.Equal(5, Rcode(reply));
        Assert.Equal(0, Answers(reply));
    }

    [Fact]
    public void Malformed_queries_get_no_reply()
    {
        var query = Query("secrets.dev.localhost");
        Assert.Null(LoopbackDns.Answer(query.AsSpan(0, 8)));            // shorter than a header
        Assert.Null(LoopbackDns.Answer(query.AsSpan(0, query.Length - 2)));   // question cut short
        query[2] |= 0x80;
        Assert.Null(LoopbackDns.Answer(query));                       // a response, not a query
    }

    private static LoopbackDns Responder(int port) =>
        new(Options.Create(new KvxOptions { Dns = $"127.0.0.1:{port}" }), NullLogger<LoopbackDns>.Instance);

    // Resends like a stub resolver: the responder binds in the background, and a send into the unbound gap comes
    // back as an ICMP refusal on the next receive.
    private static async Task<byte[]> Ask(int port, string name)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (true)
        {
            using var udp = new UdpClient(0, AddressFamily.InterNetwork);
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                await udp.SendAsync(Query(name), new IPEndPoint(IPAddress.Loopback, port));
                return (await udp.ReceiveAsync(wait.Token)).Buffer;
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException && DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }
        }
    }

    [Fact]
    public async Task Answers_over_udp_and_a_waiting_kvx_takes_over_when_the_first_stops()
    {
        var port = LiveKvx.FreePort();
        using var first = Responder(port);
        using var second = Responder(port);
        await first.StartAsync(default);
        await second.StartAsync(default);   // can't bind yet; keeps trying

        Assert.Equal(IPAddress.Loopback, Address(await Ask(port, "a.dev.localhost")));
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        Assert.True(await LoopbackDns.AnswersAt(endpoint, "kvx-doctor.dev.localhost", TimeSpan.FromSeconds(2)));
        Assert.False(await LoopbackDns.AnswersAt(endpoint, "example.com", TimeSpan.FromSeconds(2)));

        await first.StopAsync(default);
        Assert.Equal(IPAddress.Loopback, Address(await Ask(port, "b.dev.localhost")));   // waits out the retry
        await second.StopAsync(default);
    }

    [Fact]
    public void Dns_setting_is_off_by_default_except_on_windows_and_rejects_nonsense()
    {
        Assert.Equal(OperatingSystem.IsWindows() ? new IPEndPoint(IPAddress.Loopback, 53) : null, new KvxOptions().ResolveDns());
        Assert.Null(new KvxOptions { Dns = "off" }.ResolveDns());
        Assert.Contains("Kvx:Dns", Assert.Throws<ConfigurationException>(() => new KvxOptions { Dns = "port 53" }.ResolveDns()).Message);
    }
}
