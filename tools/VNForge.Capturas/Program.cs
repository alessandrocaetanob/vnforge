using VNForge.Capturas;

// Grava o PNG de cada tela do app, a 1440×1000, nos dois temas, com o projeto de demonstração, para o designer pôr ao lado do artboard.
// Roda fora do CI: dotnet run --project tools/VNForge.Capturas -c Release -- [--saida pasta] [--tela nome] [--tema escuro|claro]
// Sem argumentos grava todas as telas, nos dois temas, em .tmp/capturas. O nome do arquivo é <tela>-<escuro|claro>.png.
return Capturas.Run(args);
