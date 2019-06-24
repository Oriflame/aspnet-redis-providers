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
        private IVersionCheckInterceptor versionCheckInterceptor = NoVersionCheckInterceptor.Instance;

        public override void Initialize(string name, NameValueCollection config)
        {
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

        public override async Task<GetItemResult> GetItemAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            var result = await base.GetItemAsync(context, id, cancellationToken);
            return await SanitizeSessionByVersion(context, id, result, false, cancellationToken).ConfigureAwait(false);
        }

        public override async Task<GetItemResult> GetItemExclusiveAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            var result = await base.GetItemExclusiveAsync(context, id, cancellationToken).ConfigureAwait(false);
            return await SanitizeSessionByVersion(context, id, result, true, cancellationToken).ConfigureAwait(false);
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