using ExpControlsLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WindowsApiLib.Shell;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace WindowsApiLibTest
{
    [TestClass]
    public static class AssemblyInitializer
    {
        private static StaThreadRunner _runner;
        public static StaThreadRunner Runner => _runner;

        [AssemblyInitialize]
        public static void AssemblyInit(TestContext context)
        {
            _runner = new StaThreadRunner(1, "Global STA Test Runner");
            _runner.EnqueueWork(() =>
            {
                CShellItemFactory.Initialize();
            }).Wait();
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            _runner?.Dispose();
        }
    }
}
