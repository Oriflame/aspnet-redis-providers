using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;
using Microsoft.AspNet.SessionState;

namespace Oriflame.Web.Redis
{
    public interface ISessionVersionProvider
    {
        void SetVersion(ISessionStateItemCollection sessionData);
        Task<GetItemResult> SanitizeSessionByVersion(HttpContextBase context, string id, GetItemResult result, bool isResultExclusivelyLocked, CancellationToken cancellationToken);
        void Initialize(SessionStateStoreProviderAsyncBase sessionStateStoreProvider, NameValueCollection config);
    }
}