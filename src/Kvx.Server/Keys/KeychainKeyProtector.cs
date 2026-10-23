using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Kvx.Server.Keys;

/// <summary>
/// macOS: a random key-encryption key kept as a generic password in the user's login keychain wraps the data key
/// with AES-GCM, the credential name bound in as associated data. The item trusts any app of the user, as DPAPI and
/// `systemd-creds --user` do: trusting only the creating binary would make every tool update ask again, and a read
/// that needs to ask hangs without a UI. User interaction is turned off, so a locked keychain fails instead of hanging.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class KeychainKeyProtector : IKeyProtector
{
    private const byte Format = 1;
    private const int NonceSize = 12, TagSize = 16, KekSize = 32;
    private const string Service = "kvx";

    private readonly string _account;
    private readonly byte[] _aad;

    public KeychainKeyProtector(string credentialName)
    {
        _account = credentialName;
        _aad = Encoding.UTF8.GetBytes(credentialName);
        Keychain.SecKeychainSetUserInteractionAllowed(false);
    }

    private string Item => $"'{_account}' (service '{Service}') in the login keychain";

    public byte[] Wrap(byte[] key)
    {
        var kek = Read() ?? Create();
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            var ciphertext = new byte[key.Length];
            using (var aes = new AesGcm(kek, TagSize))
                aes.Encrypt(nonce, key, ciphertext, tag, _aad);
            return [Format, .. nonce, .. tag, .. ciphertext];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    public byte[] Unwrap(byte[] wrapped)
    {
        if (wrapped.Length < 1 + NonceSize + TagSize || wrapped[0] != Format)
            throw new KeyProtectorException("The wrapped data key was not made by the keychain protector.");
        var kek = Read() ?? throw new KeyProtectorException($"There is no {Item}, so the data key cannot be unwrapped.");
        try
        {
            var body = wrapped.AsSpan(1);
            var key = new byte[body.Length - NonceSize - TagSize];
            using var aes = new AesGcm(kek, TagSize);
            aes.Decrypt(body[..NonceSize], body[(NonceSize + TagSize)..], body.Slice(NonceSize, TagSize), key, _aad);
            return key;
        }
        catch (AuthenticationTagMismatchException e)
        {
            throw new KeyProtectorException($"The data key does not unwrap with the key in {Item}.", e);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private byte[]? Read()
    {
        using var cf = new Keychain.Scope();
        var query = cf.Dictionary(
            (Keychain.Const("kSecClass"), Keychain.Const("kSecClassGenericPassword")),
            (Keychain.Const("kSecAttrService"), cf.String(Service)),
            (Keychain.Const("kSecAttrAccount"), cf.String(_account)),
            (Keychain.Const("kSecReturnData"), Keychain.Const("kCFBooleanTrue", Keychain.CoreFoundation)),
            (Keychain.Const("kSecMatchLimit"), Keychain.Const("kSecMatchLimitOne")));
        var status = Keychain.SecItemCopyMatching(query, out var data);
        if (status == Keychain.ItemNotFound) return null;
        if (status != 0) throw Failure("read", status);
        cf.Own(data);
        return Keychain.Bytes(data);
    }

    private byte[] Create()
    {
        var kek = RandomNumberGenerator.GetBytes(KekSize);
        using var cf = new Keychain.Scope();
        var attributes = cf.Dictionary(
            (Keychain.Const("kSecClass"), Keychain.Const("kSecClassGenericPassword")),
            (Keychain.Const("kSecAttrService"), cf.String(Service)),
            (Keychain.Const("kSecAttrAccount"), cf.String(_account)),
            (Keychain.Const("kSecAttrLabel"), cf.String($"kvx key-encryption key ({_account})")),
            (Keychain.Const("kSecValueData"), cf.Data(kek)),
            (Keychain.Const("kSecAttrAccess"), AnyAppAccess(cf)));
        var status = Keychain.SecItemAdd(attributes, IntPtr.Zero);
        if (status == 0) return kek;
        CryptographicOperations.ZeroMemory(kek);
        // Another kvx created it first: use theirs.
        if (status == Keychain.DuplicateItem) return Read() ?? throw Failure("read", Keychain.ItemNotFound);
        throw Failure("create", status);
    }

    /// <summary>Access whose every ACL except the one guarding ACL changes names no apps, which means any app.</summary>
    private static IntPtr AnyAppAccess(Keychain.Scope cf)
    {
        Check("create access for", Keychain.SecAccessCreate(cf.String("kvx"), IntPtr.Zero, out var access));
        cf.Own(access);
        Check("list the ACLs of", Keychain.SecAccessCopyACLList(access, out var acls));
        cf.Own(acls);
        var changeAcl = Keychain.Const("kSecACLAuthorizationChangeACL");
        for (nint i = 0, n = Keychain.CFArrayGetCount(acls); i < n; i++)
        {
            var acl = Keychain.CFArrayGetValueAtIndex(acls, i);
            var authorizations = cf.Own(Keychain.SecACLCopyAuthorizations(acl));
            var guardsChanges = false;
            for (nint j = 0, m = Keychain.CFArrayGetCount(authorizations); j < m; j++)
                guardsChanges |= Keychain.CFEqual(Keychain.CFArrayGetValueAtIndex(authorizations, j), changeAcl);
            if (guardsChanges) continue;
            Check("read an ACL of", Keychain.SecACLCopyContents(acl, out var apps, out var description, out var prompt));
            cf.Own(apps);
            cf.Own(description);
            Check("open an ACL of", Keychain.SecACLSetContents(acl, IntPtr.Zero, description, prompt));
        }
        return access;

        static void Check(string what, int status)
        {
            if (status != 0) throw Failure($"{what}", status, "the keychain item");
        }
    }

    private KeyProtectorException Failure(string verb, int status) => Failure(verb, status, Item);

    private static KeyProtectorException Failure(string verb, int status, string item) => new(
        $"Could not {verb} {item}: {Keychain.Describe(status)} ({status})." +
        (status == Keychain.InteractionNotAllowed
            ? " The login keychain is locked (as over SSH); unlock it with `security unlock-keychain`."
            : ""));
}

/// <summary>The few Security.framework and CoreFoundation calls the keychain protector needs.</summary>
[SupportedOSPlatform("macos")]
internal static class Keychain
{
    public const string Security = "/System/Library/Frameworks/Security.framework/Security";
    public const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public const int ItemNotFound = -25300, DuplicateItem = -25299, InteractionNotAllowed = -25308;
    private const uint Utf8 = 0x08000100;

    private static readonly Lazy<IntPtr> SecurityLib = new(() => NativeLibrary.Load(Security));
    private static readonly Lazy<IntPtr> CoreFoundationLib = new(() => NativeLibrary.Load(CoreFoundation));

    /// <summary>The value of an exported constant such as kSecClass (exports are pointers to the CFTypeRef).</summary>
    public static IntPtr Const(string name, string library = Security) => Marshal.ReadIntPtr(Export(name, library));

    private static IntPtr Export(string name, string library) =>
        NativeLibrary.GetExport(library == Security ? SecurityLib.Value : CoreFoundationLib.Value, name);

    public static byte[] Bytes(IntPtr data)
    {
        var bytes = new byte[CFDataGetLength(data)];
        Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, bytes.Length);
        return bytes;
    }

    public static string Describe(int status)
    {
        var message = SecCopyErrorMessageString(status, IntPtr.Zero);
        if (message == IntPtr.Zero) return "unknown error";
        try
        {
            var buffer = new byte[1024];
            return CFStringGetCString(message, buffer, buffer.Length, Utf8)
                ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0) is var end and >= 0 ? end : buffer.Length)
                : "unknown error";
        }
        finally
        {
            CFRelease(message);
        }
    }

    /// <summary>Releases every CoreFoundation object it made or was handed, when disposed.</summary>
    public sealed class Scope : IDisposable
    {
        private readonly List<IntPtr> _owned = [];

        public IntPtr Own(IntPtr cf)
        {
            if (cf != IntPtr.Zero) _owned.Add(cf);
            return cf;
        }

        public IntPtr String(string s) => Own(CFStringCreateWithCString(IntPtr.Zero, s, Utf8));
        public IntPtr Data(byte[] bytes) => Own(CFDataCreate(IntPtr.Zero, bytes, bytes.Length));

        public IntPtr Dictionary(params (IntPtr Key, IntPtr Value)[] entries) => Own(CFDictionaryCreate(IntPtr.Zero,
            entries.Select(e => e.Key).ToArray(), entries.Select(e => e.Value).ToArray(), entries.Length,
            Export("kCFTypeDictionaryKeyCallBacks", CoreFoundation), Export("kCFTypeDictionaryValueCallBacks", CoreFoundation)));

        public void Dispose()
        {
            for (var i = _owned.Count - 1; i >= 0; i--) CFRelease(_owned[i]);
        }
    }

    [DllImport(Security)] public static extern int SecItemAdd(IntPtr attributes, IntPtr result);
    [DllImport(Security)] public static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
    [DllImport(Security)] public static extern int SecKeychainSetUserInteractionAllowed([MarshalAs(UnmanagedType.U1)] bool state);
    [DllImport(Security)] public static extern int SecAccessCreate(IntPtr descriptor, IntPtr trustedList, out IntPtr access);
    [DllImport(Security)] public static extern int SecAccessCopyACLList(IntPtr access, out IntPtr aclList);
    [DllImport(Security)] public static extern IntPtr SecACLCopyAuthorizations(IntPtr acl);
    [DllImport(Security)] public static extern int SecACLCopyContents(IntPtr acl, out IntPtr applicationList, out IntPtr description, out ushort promptSelector);
    [DllImport(Security)] public static extern int SecACLSetContents(IntPtr acl, IntPtr applicationList, IntPtr description, ushort promptSelector);
    [DllImport(Security)] private static extern IntPtr SecCopyErrorMessageString(int status, IntPtr reserved);

    [DllImport(CoreFoundation)] public static extern void CFRelease(IntPtr cf);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool CFEqual(IntPtr a, IntPtr b);
    [DllImport(CoreFoundation)] public static extern nint CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] public static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
    [DllImport(CoreFoundation)] private static extern nint CFDataGetLength(IntPtr data);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string s, uint encoding);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool CFStringGetCString(IntPtr s, byte[] buffer, nint size, uint encoding);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallBacks, IntPtr valueCallBacks);
}
