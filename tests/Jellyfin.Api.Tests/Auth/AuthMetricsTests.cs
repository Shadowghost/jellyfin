using System.Threading.Tasks;
using Jellyfin.Api.Auth;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Session;
using Xunit;

namespace Jellyfin.Api.Tests.Auth
{
    // Single class so these tests run sequentially (they read/modify the shared global metric registry).
    public class AuthMetricsTests
    {
        [Fact]
        public void RecordAttempt_Success_IncrementsSchemeSuccess()
        {
            var counter = AuthMetrics.Attempts.WithLabels(AuthMetrics.SchemeJwt, "success");
            var before = counter.Value;

            AuthMetrics.RecordAttempt(AuthMetrics.SchemeJwt, true);

            Assert.Equal(before + 1, counter.Value);
        }

        [Fact]
        public void RecordAttempt_Failure_IncrementsSchemeFailure()
        {
            var counter = AuthMetrics.Attempts.WithLabels(AuthMetrics.SchemeLegacy, "failure");
            var before = counter.Value;

            AuthMetrics.RecordAttempt(AuthMetrics.SchemeLegacy, false);

            Assert.Equal(before + 1, counter.Value);
        }

        [Fact]
        public void RecordLogin_TracksSuccessAndFailureSeparately()
        {
            var success = AuthMetrics.Logins.WithLabels("success");
            var failure = AuthMetrics.Logins.WithLabels("failure");
            var beforeSuccess = success.Value;
            var beforeFailure = failure.Value;

            AuthMetrics.RecordLogin(true);

            Assert.Equal(beforeSuccess + 1, success.Value);
            Assert.Equal(beforeFailure, failure.Value);
        }

        [Fact]
        public void TempTokenIssuedAndRevoked_Increment()
        {
            var beforeIssued = AuthMetrics.TempTokensIssued.Value;
            var beforeRevoked = AuthMetrics.TempTokensRevoked.Value;

            AuthMetrics.TempTokenIssued();
            AuthMetrics.TempTokenRevoked();

            Assert.Equal(beforeIssued + 1, AuthMetrics.TempTokensIssued.Value);
            Assert.Equal(beforeRevoked + 1, AuthMetrics.TempTokensRevoked.Value);
        }

        [Fact]
        public async Task LoginConsumer_ResultEvent_RecordsSuccess()
        {
            var counter = AuthMetrics.Logins.WithLabels("success");
            var before = counter.Value;

            await new AuthLoginMetricsConsumer().OnEvent(new AuthenticationResultEventArgs(new AuthenticationResult()));

            Assert.Equal(before + 1, counter.Value);
        }

        [Fact]
        public async Task LoginConsumer_RequestEvent_RecordsFailure()
        {
            var counter = AuthMetrics.Logins.WithLabels("failure");
            var before = counter.Value;

            await new AuthLoginMetricsConsumer().OnEvent(new AuthenticationRequestEventArgs(new AuthenticationRequest()));

            Assert.Equal(before + 1, counter.Value);
        }
    }
}
