  In this project, the primary reason for using async Task is Apartment State (STA vs MTA):

   1. STA Requirement: Most Windows Shell APIs and COM objects (which this project heavily uses) require a
      Single-Threaded Apartment (STA). Standard test runners (MSTest, xUnit, NUnit) typically execute tests on MTA
      (Multi-Threaded Apartment) threads.
   2. StaThreadRunner Integration: To solve the STA requirement, we use StaThreadRunner, which manages a dedicated
      thread in STA mode with a message pump. Its API (InvokeAsync) returns a Task because it's delegating work to
      another thread.
   3. Idiomatic Awaiting: While we could use InvokeAsync(...).Wait() or GetResult(), making the test method async Task
      is the modern, idiomatic way to handle a Task in MSTest. It allows the runner to handle exceptions naturally
      (without AggregateException wrapping) and makes the code cleaner.

  So, the async isn't for scaling or parallelizing the tests themselves, but simply because the mechanism we need for
  STA compatibility happens to be asynchronous.