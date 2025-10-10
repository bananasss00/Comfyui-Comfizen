using System.Collections.Specialized;
using System.Linq;
using System.Web;

namespace Comfizen;

public static class WebExtensions
{
    public static string ToQueryString(this NameValueCollection nvc)
    {
        return string.Join("&", nvc.AllKeys.Select(key => $"{HttpUtility.UrlEncode((string?)key)}={HttpUtility.UrlEncode(nvc[(string?)key])}"));
    }
}