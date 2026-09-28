using System.ServiceProcess;

namespace NullService
{
    /// <summary>
    /// A trivial do-nothing Windows service, installed as the disposable
    /// "ZZTestSvc" via the exerciser's Setup entry (`sc create`). Answers
    /// SCM start/stop/pause/continue requests and does nothing else - it
    /// exists purely as a safe target for ServiceUtils' interactive cases,
    /// never a real system service (per TESTING.md's explicit warning).
    /// </summary>
    public class NullService : ServiceBase
    {
        public NullService()
        {
            ServiceName = "ZZTestSvc";
            CanPauseAndContinue = true;
        }

        protected override void OnStart(string[] args)
        {
        }

        protected override void OnStop()
        {
        }

        protected override void OnPause()
        {
        }

        protected override void OnContinue()
        {
        }

        private static void Main()
        {
            Run(new NullService());
        }
    }
}
