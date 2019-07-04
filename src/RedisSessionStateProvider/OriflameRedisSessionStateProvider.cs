using System;
using System.Collections.Specialized;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;
using Microsoft.AspNet.SessionState;

namespace Oriflame.Web.Redis
{
    public class RedisSessionStateProvider : Microsoft.Web.Redis.RedisSessionStateProvider
    {
        private class NoVersionCheckInterceptor : IVersionCheckInterceptor
        {
            public static readonly NoVersionCheckInterceptor Instance = new NoVersionCheckInterceptor();

            private NoVersionCheckInterceptor()
            {
            }

            public void SetVersion(ISessionStateItemCollection sessionData)
            {
            }

            public Task<GetItemResult> SanitizeSessionByVersion(HttpContextBase context, string id, GetItemResult result, bool isResultExclusivelyLocked, CancellationToken cancellationToken)
            {
                return Task.FromResult(result);
            }

            public void Initialize(SessionStateStoreProviderAsyncBase sessionStateStoreProvider, NameValueCollection config)
            {
            }
        }

        public const string SessionVersionProviderTypeAttributeName = "SessionVersionProviderType";
        internal const string SessionEndPollingIntervalKey = "SessionEndPollingInterval";
        private IVersionCheckInterceptor versionCheckInterceptor = NoVersionCheckInterceptor.Instance;
        private static bool isInitializedStatically;
        private static readonly object staticLock = new object();
        private static MemoryCache localCache;
        private SessionStateItemExpireCallback expireCallback;

        public override void Initialize(string name, NameValueCollection config)
        {
            if (!isInitializedStatically)
            {
                lock (staticLock)
                {
                    if (!isInitializedStatically)
                    {
                        InitializeStatically(config);
                        isInitializedStatically = true;
                    }
                }
            }

            base.Initialize(name, config);

            var sessionVersionProviderTypeName = config[SessionVersionProviderTypeAttributeName];
            if (!string.IsNullOrEmpty(sessionVersionProviderTypeName))
            {
                // todo throw custom error
                var versionProviderType = Type.GetType(sessionVersionProviderTypeName, true);
                versionCheckInterceptor = (IVersionCheckInterceptor)Activator.CreateInstance(versionProviderType);
                versionCheckInterceptor.Initialize(this, config);
            }
        }

        internal static void InitializeStatically(NameValueCollection config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            var configCollection = new NameValueCollection();
            var pollingInterval = config[SessionEndPollingIntervalKey];
            if (!string.IsNullOrEmpty(pollingInterval))
            {
                configCollection["pollingInterval"] = pollingInterval;
            }

            localCache?.Dispose();

            localCache = new MemoryCache("RedisSessionStateProvider", configCollection);
        }

        public override Task CreateUninitializedItemAsync(HttpContextBase context, string id, int timeout, CancellationToken cancellationToken)
        {
            UpdateLocalCache(timeout, id);
            return base.CreateUninitializedItemAsync(context, id, timeout, cancellationToken);
        }

        public override async Task<GetItemResult> GetItemAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            var result = await base.GetItemAsync(context, id, cancellationToken).ConfigureAwait(false);
            var timeout = result.Item?.Timeout;
            if (timeout.HasValue)
            {
                UpdateLocalCache(timeout.Value, id);
            }
            return await SanitizeSessionByVersion(context, id, result, false, cancellationToken).ConfigureAwait(false);
        }

        public override async Task<GetItemResult> GetItemExclusiveAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            var result = await base.GetItemExclusiveAsync(context, id, cancellationToken).ConfigureAwait(false);
            var timeout = result.Item?.Timeout;
            if (timeout.HasValue)
            {
                UpdateLocalCache(timeout.Value, id);
            }

            return await SanitizeSessionByVersion(context, id, result, true, cancellationToken).ConfigureAwait(false);
        }

        public override Task SetAndReleaseItemExclusiveAsync(HttpContextBase context, string id, SessionStateStoreData item, object lockId, bool newItem, CancellationToken cancellationToken)
        {
            UpdateLocalCache(context.Session.Timeout, id);
            SetVersion(item.Items);
            return base.SetAndReleaseItemExclusiveAsync(context, id, item, lockId, newItem, cancellationToken);
        }

        public override Task ReleaseItemExclusiveAsync(HttpContextBase context, string id, object lockId, CancellationToken cancellationToken)
        {
            UpdateLocalCache(context.Session.Timeout, id); // TODO maybe not call, verify
            return base.ReleaseItemExclusiveAsync(context, id, lockId, cancellationToken);
        }

        public override Task ResetItemTimeoutAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            UpdateLocalCache(context.Session.Timeout, id);
            return base.ResetItemTimeoutAsync(context, id, cancellationToken);
        }

        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            this.expireCallback = expireCallback;

            return true;
        }

        protected override void OnCreateUninitializedItemAsync(ISessionStateItemCollection sessionData)
        {
            SetVersion(sessionData);
            base.OnCreateUninitializedItemAsync(sessionData);
        }

        internal virtual TimeSpan FromMinutes(int minutes)
        {
            return TimeSpan.FromMinutes(minutes);
        }

        private void UpdateLocalCache(int timeout, string id)
        {
            var cachePolicy = new CacheItemPolicy
            {
                RemovedCallback = OnSessionExpired,
                SlidingExpiration = FromMinutes(timeout)
            };

            localCache.AddOrGetExisting(id, id, cachePolicy);
        }

        private void OnSessionExpired(CacheEntryRemovedArguments arguments)
        {
            if (expireCallback == null)
            {
                return;
            }

            if (arguments.RemovedReason != CacheEntryRemovedReason.Expired
                && arguments.RemovedReason != CacheEntryRemovedReason.CacheSpecificEviction)
            {
                return;
            }

            var id = arguments.CacheItem.Key;
            GetAccessToStore(id);
            var requestTimeout = configuration.RequestTimeout.TotalSeconds;
            var expiration = cache.GetRemainingExpiration();
            if (!cache.TryTakeWriteLockAndGetData(DateTime.Now, (int) requestTimeout, out var lockId, out var data, out var sessionTimeout))
            {
                return;
            }

            if (data == null)
            {
                return;
            }

            if (expiration > TimeSpan.FromSeconds(1))
            {
                return;
            }

            var item = new SessionStateStoreData(data, new HttpStaticObjectsCollection(), sessionTimeout);
            cache.TryRemoveAndReleaseLock(lockId);

            // Expire callback is called intentionally AFTER removing session from Redis
            // so that other servers potentially waiting for session lock
            // are not forced to wait until expireCallback is finished
            expireCallback(id, item);
        }

        private Task<GetItemResult> SanitizeSessionByVersion(
            HttpContextBase context,
            string id,
            GetItemResult result,
            bool isResultExclusivelyLocked,
            CancellationToken cancellationToken)
        {
            return versionCheckInterceptor.SanitizeSessionByVersion(context, id, result, isResultExclusivelyLocked, cancellationToken);
        }

        private void SetVersion(ISessionStateItemCollection sessionData)
        {
            if (sessionData == null)
            {
                return;
            }

            versionCheckInterceptor.SetVersion(sessionData);
        }
    }
}