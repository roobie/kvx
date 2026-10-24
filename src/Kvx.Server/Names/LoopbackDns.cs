using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace Kvx.Server.Names;

/// <summary>
/// A DNS responder that answers every name under `localhost` with 127.0.0.1, as RFC 6761 asks of resolvers. Windows
/// doesn't, so `kvx doctor` asks for one NRPT rule sending `.localhost` here, and vaults then resolve as they appear,
/// without a hosts line each. Every kvx gives the same answers, so one that can't bind keeps trying and takes over when
/// the holder stops; a port held by another program (ICS) only leaves the hosts file as the way.
/// </summary>
public sealed class LoopbackDns(IOptions<KvxOptions> options, ILogger<LoopbackDns> log) : BackgroundService
{
    // Short, since names don't resolve between one kvx stopping and the next taking over; a failed bind costs little.
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (options.Value.ResolveDns() is not { } endpoint) return;
        var reported = false;
        while (!stop.IsCancellationRequested)
        {
            UdpClient udp;
            try { udp = new UdpClient(endpoint); }
            catch (SocketException e)
            {
                if (!reported) log.LogInformation("Not answering DNS on {Endpoint} ({Reason}); retrying while kvx runs.", endpoint, e.Message);
                reported = true;
                try { await Task.Delay(Retry, stop); } catch (OperationCanceledException) { }
                continue;
            }
            using (udp)
            {
                log.LogInformation("Answering *.localhost with loopback on udp {Endpoint}.", endpoint);
                await Serve(udp, stop);
            }
        }
    }

    private static async Task Serve(UdpClient udp, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            UdpReceiveResult query;
            try { query = await udp.ReceiveAsync(stop); }
            catch (OperationCanceledException) { return; }
            catch (SocketException) { continue; }   // Windows reports an earlier reply's ICMP unreachable here
            if (Answer(query.Buffer) is { } reply)
                try { await udp.SendAsync(reply, query.RemoteEndPoint, stop); }
                catch (SocketException) { }
        }
    }

    /// <summary>Whether the responder answers for this name.</summary>
    public static bool Covers(string name) =>
        name.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a responder at the endpoint answers an A query for the name with loopback, asked directly.</summary>
    public static async Task<bool> AnswersAt(IPEndPoint endpoint, string name, TimeSpan timeout)
    {
        var query = new List<byte> { 0x6b, 0x76, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in name.Split('.'))
        {
            query.Add((byte)label.Length);
            query.AddRange(Encoding.ASCII.GetBytes(label));
        }
        query.AddRange([0, 0, (byte)TypeA, 0, (byte)ClassIn]);
        try
        {
            using var udp = new UdpClient(endpoint.AddressFamily);
            using var wait = new CancellationTokenSource(timeout);
            await udp.SendAsync(query.ToArray(), endpoint, wait.Token);
            var reply = (await udp.ReceiveAsync(wait.Token)).Buffer;
            return reply.Length >= query.Count + 16 && (reply[3] & 0x0F) == 0 &&
                   BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(6)) > 0 &&
                   new IPAddress(reply.AsSpan(reply.Length - 4)).Equals(IPAddress.Loopback);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException) { return false; }
    }

    private const int Header = 12;
    private const ushort TypeA = 1, ClassIn = 1;
    private const int RcodeRefused = 5;

    /// <summary>The reply to one query, or null for anything that isn't a single-question query.</summary>
    public static byte[]? Answer(ReadOnlySpan<byte> query)
    {
        if (query.Length < Header || (query[2] & 0x80) != 0 || BinaryPrimitives.ReadUInt16BigEndian(query[4..]) != 1)
            return null;

        var name = new StringBuilder();
        var i = Header;
        while (true)
        {
            if (i >= query.Length) return null;
            int length = query[i];
            if (length == 0) break;
            if (length > 63 || i + 1 + length > query.Length) return null;   // compression has no place in a question
            if (name.Length > 0) name.Append('.');
            name.Append(Encoding.ASCII.GetString(query.Slice(i + 1, length)));
            i += 1 + length;
        }
        var questionEnd = i + 1 + 4;
        if (questionEnd > query.Length) return null;
        var type = BinaryPrimitives.ReadUInt16BigEndian(query[(i + 1)..]);
        var @class = BinaryPrimitives.ReadUInt16BigEndian(query[(i + 3)..]);

        var ours = Covers(name.ToString());
        // A only: kvx listens on 127.0.0.1, and an AAAA answer would send clients to a ::1 nothing listens on.
        var answer = ours && type == TypeA && @class == ClassIn;

        var reply = new byte[questionEnd + (answer ? 16 : 0)];
        query[..questionEnd].CopyTo(reply);
        var flags = BinaryPrimitives.ReadUInt16BigEndian(query[2..]);
        // response, opcode and RD echoed, authoritative, recursion available; refuse what isn't under localhost
        flags = (ushort)(0x8000 | (flags & 0x7900) | 0x0400 | 0x0080 | (ours ? 0 : RcodeRefused));
        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(2), flags);
        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(6), (ushort)(answer ? 1 : 0));
        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(10), 0);
        if (answer)
        {
            var record = reply.AsSpan(questionEnd);
            BinaryPrimitives.WriteUInt16BigEndian(record, 0xC000 | Header);   // the name: a pointer to the question's
            BinaryPrimitives.WriteUInt16BigEndian(record[2..], TypeA);
            BinaryPrimitives.WriteUInt16BigEndian(record[4..], ClassIn);
            BinaryPrimitives.WriteUInt32BigEndian(record[6..], 60);
            BinaryPrimitives.WriteUInt16BigEndian(record[10..], 4);
            IPAddress.Loopback.GetAddressBytes().CopyTo(record[12..]);
        }
        return reply;
    }
}
