// A sessão headless tem uma thread de UI só, e o isolamento por teste recria o Application a cada Dispatch.
// Com as classes em paralelo (uma thread de xUnit por CPU), cada teste ficava na fila atrás dos casos pesados de render:
// ButtonStyleTests levava 3 s sozinha e aparecia com 500 s no JUnit. O tempo de parede não melhora com o paralelismo
// (a suíte inteira levou 906 s em série e 968 s em paralelo), e o JUnit com espera de fila desbalanceia o
// `circleci tests split --split-by=timings`. Em série, o tempo de cada teste é o dele.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
