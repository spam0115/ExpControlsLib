using ExpControlsLib;
using NUnit.Framework;
using WindowsApiLib.Shell;

namespace ExpControlsLibTest
{
    [SetUpFixture]
    public class AssemblyInitializer
    {
        private static StaThreadRunner? _runner;
        public static StaThreadRunner Runner => _runner ?? throw new InvalidOperationException("Runner not initialized");

        [OneTimeSetUp]
        public void Setup()
        {
            _runner = new StaThreadRunner(1, "Global STA Test Runner");
            _runner.EnqueueWork(() =>
            {
                ShellController.Initialize();
            }).Wait();
        }

        [OneTimeTearDown]
        public void Teardown()
        {
            _runner?.Dispose();
        }
    }
}
