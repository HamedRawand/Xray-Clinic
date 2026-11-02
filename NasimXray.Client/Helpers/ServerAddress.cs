namespace NasimXray.Client.Helpers;

// The "server address" the client stores must be only the server itself - scheme, host and port - because every
// request is made relative to it ("api/auth/login", "api/system/version", ...). People naturally paste a full link they
// have just opened in a browser (https://host:5443/api/system/clinic); keeping that path would send every call to a
// wrong URL and sign-in would fail. This reduces whatever was typed to https://host:port/ .
public static class ServerAddress
{
    // Returns "scheme://host[:port]/" or null when the text is not a usable http(s) address.
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var text = input.Trim();
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text; // "192.168.1.16:5443" -> https://...

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
        if (string.IsNullOrEmpty(uri.Host)) return null;

        // Rebuilt from scheme + host + port only: drops any path, query, fragment or user:password@ part.
        return new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port).Uri.AbsoluteUri;
    }
}
