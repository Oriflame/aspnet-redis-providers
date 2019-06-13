using System.Collections.Specialized;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;
using Microsoft.AspNet.SessionState;

namespace Oriflame.Web.Redis
{
    public class SessionVersionFromApplicationProvider : ISessionVersionProvider
    {
        private SessionStateStoreProviderAsyncBase sessionStateStoreProvider;
        public const string SessionVersionKey = nameof(SessionVersionFromApplicationProvider) + ".Version";
        public const string VersionConfigAttributeName = "version";

        private static string _applicationVersion;

        public void SetVersion(ISessionStateItemCollection sessionData)
        {
            sessionData[SessionVersionKey] = _applicationVersion;
        }

        public Task<GetItemResult> SanitizeSessionByVersion(
            HttpContextBase context,
            string id,
            GetItemResult result,
            CancellationToken cancellationToken)
        {
            var sessionVersion = result.Item?.Items[SessionVersionKey] as string;
            if (_applicationVersion == sessionVersion)
            {
                return Task.FromResult(result);
            }

            // TODO remove from Redis now of wait for request end?
            //await sessionStateStoreProvider.RemoveItemAsync(context, id, result.LockId, result.Item, cancellationToken); // TODO lockId always initialized?

            result.Item?.Items.Clear();
            return Task.FromResult(result);
        }

        public void Initialize(SessionStateStoreProviderAsyncBase sessionStateStoreProvider, NameValueCollection config)
        {
            this.sessionStateStoreProvider = sessionStateStoreProvider;
            var versionConfig = config[VersionConfigAttributeName];
            if (string.IsNullOrEmpty(versionConfig))
            {
                versionConfig = "auto";
            }

            _applicationVersion = InitializeVersion(versionConfig);
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
                //_log.Warn("Unable to get web application version for HybridSessionStateProvider, using 'auto'.");
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

        //_usesVersioning = !string.IsNullOrEmpty(_applicationVersion);
        //_log.InfoFormat("HybridSessionStateProvider version set to {0}.", _applicationVersion);
    }
}