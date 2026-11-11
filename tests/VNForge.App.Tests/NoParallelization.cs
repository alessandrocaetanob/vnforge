// A sessão headless é PerAssembly: um Application só, numa thread de UI só, aberta por HeadlessSessionFixture antes do primeiro teste.
// Cada Dispatch entra nessa thread, então as classes não ganham nada rodando em paralelo (uma thread de xUnit por CPU só formava fila atrás
// dos casos pesados de tela) e o JUnit com espera de fila desbalanceia o `circleci tests split --split-by=timings`.
// Em série, o tempo de cada teste é o dele.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
[assembly: Xunit.AssemblyFixture(typeof(VNForge.App.Tests.HeadlessSessionFixture))]
