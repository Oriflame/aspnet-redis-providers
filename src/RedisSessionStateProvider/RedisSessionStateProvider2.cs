using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;
using Microsoft.AspNet.SessionState;

namespace Oriflame.Web.Redis
{
    public class RedisSessionStateProvider : Microsoft.Web.Redis.RedisSessionStateProvider
    {
        private class IgnoreSessionVersionProvider : ISessionVersionProvider
        {
            public static readonly IgnoreSessionVersionProvider Instance = new IgnoreSessionVersionProvider();

            private IgnoreSessionVersionProvider()
            {
            }

            public void SetVersion(ISessionStateItemCollection sessionData)
            {
            }

            public Task<GetItemResult> SanitizeSessionByVersion(HttpContextBase context, string id, GetItemResult result, CancellationToken cancellationToken)
            {
                return Task.FromResult(result);
            }

            public void Initialize(SessionStateStoreProviderAsyncBase sessionStateStoreProvider, NameValueCollection config)
            {
            }
        }

        public const string SessionVersionProviderTypeAttributeName = "SessionVersionProviderType";
        private ISessionVersionProvider versionProvider = IgnoreSessionVersionProvider.Instance;

        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(name, config);

            var sessionVersionProviderTypeName = config[SessionVersionProviderTypeAttributeName];
            if (!string.IsNullOrEmpty(sessionVersionProviderTypeName))
            {
                // todo throw custom error
                var versionProviderType = Type.GetType(sessionVersionProviderTypeName, true);
                versionProvider = (ISessionVersionProvider)Activator.CreateInstance(versionProviderType);
                versionProvider.Initialize(this, config);
            }
        }

        public override async Task<GetItemResult> GetItemAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            var result = await base.GetItemAsync(context, id, cancellationToken);

            result = await SanitizeSessionByVersion(context, id, result, cancellationToken).ConfigureAwait(false);

            return result;
        }

        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            return base.SetItemExpireCallback(expireCallback);
        }

        public override SessionStateStoreData CreateNewStoreData(HttpContextBase context, int timeout)
        {
            return base.CreateNewStoreData(context, timeout);
        }

        public override Task CreateUninitializedItemAsync(HttpContextBase context, string id, int timeout, CancellationToken cancellationToken)
        {
            return base.CreateUninitializedItemAsync(context, id, timeout, cancellationToken);
        }

        public override Task EndRequestAsync(HttpContextBase context)
        {
            return base.EndRequestAsync(context);
        }

        public override async Task<GetItemResult> GetItemExclusiveAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            var result = await base.GetItemExclusiveAsync(context, id, cancellationToken).ConfigureAwait(false);

            return await SanitizeSessionByVersion(context, id, result, cancellationToken).ConfigureAwait(false);
        }

        public override Task ReleaseItemExclusiveAsync(HttpContextBase context, string id, object lockId, CancellationToken cancellationToken)
        {
            return base.ReleaseItemExclusiveAsync(context, id, lockId, cancellationToken);
        }

        public override Task RemoveItemAsync(HttpContextBase context, string id, object lockId, SessionStateStoreData item, CancellationToken cancellationToken)
        {
            return base.RemoveItemAsync(context, id, lockId, item, cancellationToken);
        }

        public override Task ResetItemTimeoutAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            return base.ResetItemTimeoutAsync(context, id, cancellationToken);
        }

        public override Task SetAndReleaseItemExclusiveAsync(HttpContextBase context, string id, SessionStateStoreData item, object lockId, bool newItem, CancellationToken cancellationToken)
        {
            SetVersion(item.Items);
            return base.SetAndReleaseItemExclusiveAsync(context, id, item, lockId, newItem, cancellationToken);
        }

        protected override void OnCreateUninitializedItemAsync(ISessionStateItemCollection sessionData)
        {
            SetVersion(sessionData);
            base.OnCreateUninitializedItemAsync(sessionData);
        }

        private Task<GetItemResult> SanitizeSessionByVersion(HttpContextBase context, string id, GetItemResult result, CancellationToken cancellationToken)
        {
            return versionProvider.SanitizeSessionByVersion(context, id, result, cancellationToken);
        }

        private void SetVersion(ISessionStateItemCollection sessionData)
        {
            if (sessionData == null)
            {
                return;
            }

            versionProvider.SetVersion(sessionData);
        }
    }
}