using System;
using ServiceStack.Html;
using ServiceStack.Web;

namespace ServiceStack
{
    /// <summary>
    /// Cache the Response of a Service
    /// </summary>
    public class CacheResponseAttribute : RequestFilterAttribute
    {
        /// <summary>
        /// Cache expiry in seconds
        /// </summary>
        public int Duration { get; set; }

        /// <summary>
        /// MaxAge in seconds
        /// </summary>
        public int MaxAge { get; set; }

        /// <summary>
        /// Cache-Control HTTP Headers
        /// </summary>
        public CacheControl CacheControl { get; set; }

        /// <summary>
        /// Vary cache per user
        /// </summary>
        public bool VaryByUser { get; set; }

        /// <summary>
        /// Vary cache for users in these roles
        /// </summary>
        public string[] VaryByRoles { get; set; }

        /// <summary>
        /// Use HostContext.LocalCache or HostContext.Cache
        /// </summary>
        public bool LocalCache { get; set; }

        public CacheResponseAttribute()
        {
            MaxAge = -1;
        }

        public override void Execute(IRequest req, IResponse res, object requestDto)
        {
            if (req.Verb != HttpMethods.Get && req.Verb != HttpMethods.Head)
                return;

            var feature = HostContext.GetPlugin<HttpCacheFeature>();
            if (feature == null)
                throw new NotSupportedException(ErrorMessages.CacheFeatureMustBeEnabled.Fmt("[CacheResponse]"));

            var keyBase = "res:" + req.RawUrl;
            var keySuffix = MimeTypes.GetExtension(req.ResponseContentType);

            var modifiers = "";
            if (req.ResponseContentType == MimeTypes.Json)
            {
                string jsonp = req.GetJsonpCallback();
                if (jsonp != null)
                    modifiers = "jsonp:" + jsonp.SafeVarName();
            }

            if (VaryByUser)
                modifiers += (modifiers.Length > 0 ? "+" : "") + "user:" + req.GetSessionId();

            if (VaryByRoles != null && VaryByRoles.Length > 0)
            {
                var userSession = req.GetSession();
                if (userSession != null)
                {
                    foreach (var role in VaryByRoles)
                    {
                        if (userSession.HasRole(role))
                            modifiers += (modifiers.Length > 0 ? "+" : "") + "role:" + role;
                    }
                }
            }

            if (modifiers.Length > 0)
                keySuffix += "+" + modifiers;

            var cacheKey = keyBase + keySuffix;
            var cacheKeyLastModified = "date:" + cacheKey;
            var cache = LocalCache ? HostContext.LocalCache : HostContext.Cache;

            var doHttpCaching = MaxAge > 0 || CacheControl != CacheControl.None;
            if (doHttpCaching)
            {
                var lastModified = cache.Get<DateTime?>(cacheKeyLastModified);
                if (req.HasValidCache(lastModified))
                {
                    res.EndNotModified();
                    return;
                }
            }

            var encoding = req.GetCompressionType();

            var responseBytes = encoding != null 
                ? cache.Get<byte[]>(cacheKey + "." + encoding) 
                : cache.Get<byte[]>(cacheKey);

            if (responseBytes != null)
            {
                if (encoding != null)
                    res.AddHeader(HttpHeaders.ContentEncoding, encoding);

                res.WriteBytesToResponse(responseBytes, req.ResponseContentType);
                return;
            }

            req.Items[Keywords.CacheInfo] = new CacheInfo
            {
                KeyBase = keyBase,
                KeyModifiers = keySuffix,
                ExpiresIn = Duration > 0 ? TimeSpan.FromSeconds(Duration) : (TimeSpan?) null,
                MaxAge = MaxAge >= 0 ? TimeSpan.FromSeconds(MaxAge) : (TimeSpan?)null,
                CacheControl = CacheControl,
                VaryByUser = VaryByUser,
                LocalCache = LocalCache,
            };
        }
    }
}