using System.Collections.Specialized;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;
using Microsoft.AspNet.SessionState;

namespace Oriflame.Web.Redis
{
    public class ApplicationVersionCheckInterceptor : IVersionCheckInterceptor
    {
        public const string SessionVersionKey = nameof(ApplicationVersionCheckInterceptor) + ".Version";
        public const string VersionConfigAttributeName = "version";

        private static string applicationVersion;
        private SessionStateStoreProviderAsyncBase sessionStateStoreProvider;

        public void SetVersion(ISessionStateItemCollection sessionData)
        {
            sessionData[SessionVersionKey] = applicationVersion;
        }

        public async Task<GetItemResult> SanitizeSessionByVersion(
            HttpContextBase context,
            string id,
            GetItemResult result,
            bool isResultExclusivelyLocked,
            CancellationToken cancellationToken)
        {
            var sessionData = result.Item?.Items;
            if (sessionData == null)
            {
                return result;
            }

            if (IsSessionVersionValid(sessionData))
            {
                return result;
            }

            if (!isResultExclusivelyLocked)
            {
                var result2 = await sessionStateStoreProvider.GetItemExclusiveAsync(context, id, cancellationToken)
                    .ConfigureAwait(false);

                if (result2.Locked)
                {
                    return result;
                }

                result = result2;
                sessionData = result2.Item.Items;
            }

            sessionData.Clear();

            // We need to call store and get (cleared) session data from Redis session stored
            // so that sessionData.Dirty == false.
            // Otherwise, Microsoft.AspNet.SessionState.SessionStateModuleAsync::InitStateStoreItem(bool addToContext)
            // will clear a dirty flag and we loose information about
            // the need to clear session data at the end of HTTP request
            await sessionStateStoreProvider.SetAndReleaseItemExclusiveAsync(context, id, result.Item, result.LockId, false, cancellationToken)
                .ConfigureAwait(false);

            if (isResultExclusivelyLocked)
            {
                return await sessionStateStoreProvider.GetItemExclusiveAsync(context, id, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                return await sessionStateStoreProvider.GetItemAsync(context, id, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        public virtual void Initialize(SessionStateStoreProviderAsyncBase sessionStateStoreProvider, NameValueCollection config)
        {
            this.sessionStateStoreProvider = sessionStateStoreProvider;
            var versionConfig = config[VersionConfigAttributeName];
            if (string.IsNullOrEmpty(versionConfig))
            {
                versionConfig = "auto";
            }

            applicationVersion = InitializeVersion(versionConfig);
        }

        protected virtual bool IsSessionVersionValid(ISessionStateItemCollection sessionData)
        {
            var sessionVersion = sessionData?[SessionVersionKey] as string;
            return applicationVersion == sessionVersion;
        }

        private static string InitializeVersion(string versionConfig)
        {
            if (versionConfig != "auto")
            {
                return versionConfig;
            }

            var appType = HttpContext.Current?.ApplicationInstance?.GetType();
            if (appType == null)
            {
                return versionConfig;
            }

            if (appType.Name == "global_asax" && appType.BaseType != null)
            {
                appType = appType.BaseType;
            }
            // use file version if available
            var fvi = FileVersionInfo.GetVersionInfo(appType.Assembly.Location);
            return !string.IsNullOrEmpty(fvi.FileVersion)
                ? fvi.FileVersion
                : appType.Assembly.GetName().Version.ToString();
        }
    }
}