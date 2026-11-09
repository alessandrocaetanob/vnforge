using System.Globalization;
using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Library;

public sealed class LibraryListModelsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("8.5", 85)]
    [InlineData("8,5", 85)]
    [InlineData("10", 100)]
    [InlineData("1", 10)]
    [InlineData(" 7 ", 70)]
    public void Nota_digitada_vira_voto_do_VNDB(string text, int vote)
    {
        Assert.Equal(vote, LibraryListRules.ParseVote(text));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.9")]
    [InlineData("10.5")]
    [InlineData("8.55")]
    [InlineData("oito")]
    [InlineData("")]
    public void Nota_fora_de_1_a_10_ou_com_mais_de_uma_casa_e_recusada(string text)
    {
        Assert.Throws<ArgumentException>(() => LibraryListRules.ParseVote(text));
    }

    [Fact]
    public void Voto_volta_para_nota_em_portugues_ou_invariante()
    {
        Assert.Equal("8,5", LibraryListRules.FormatVote(85));
        Assert.Equal("8", LibraryListRules.FormatVote(80));
        Assert.Equal("8.5", LibraryListRules.FormatVote(85, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("terminado", 2)]
    [InlineData("Finished", 2)]
    [InlineData("jogando", 1)]
    [InlineData("abandonado", 4)]
    [InlineData("lista-negra", 6)]
    [InlineData("12", 12)]
    public void Rotulo_vem_pelo_nome_em_portugues_ou_ingles_ou_pelo_id_da_conta(string text, int id)
    {
        Assert.True(LibraryListLabels.TryParse(text, out var parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("7")]
    [InlineData("voted")]
    [InlineData("1601")]
    [InlineData("qualquer")]
    public void Rotulo_voted_e_nomes_que_nao_existem_nao_valem(string text)
    {
        Assert.False(LibraryListLabels.TryParse(text, out _));
    }

    [Fact]
    public void Rotulos_de_fabrica_aparecem_em_portugues_e_os_da_conta_com_o_nome_dela()
    {
        Assert.Equal("Terminado", LibraryListLabels.Display(new VndbListLabel(2, "Finished")));
        Assert.Equal("Favoritos", LibraryListLabels.Display(new VndbListLabel(12, "Favoritos")));
    }

    [Fact]
    public void Editar_soma_e_tira_rotulos_um_a_um_e_guarda_a_hora_da_edicao()
    {
        var first = LibraryListState.Edit(null, "v4", new VndbListUpdate { LabelsSet = [1, 12] }, Now);
        var second = LibraryListState.Edit(
            first,
            "v4",
            new VndbListUpdate
            {
                LabelsSet = [2],
                LabelsUnset = [1],
                Vote = 85,
                Started = new DateOnly(2026, 1, 2),
                Notes = "Rota da Nagisa",
            },
            Now.AddHours(1));

        Assert.Equal([2, 12], second.Fields.Labels.Select(l => l.Id));
        Assert.Equal(85, second.Fields.Vote);
        Assert.Equal(new DateOnly(2026, 1, 2), second.Fields.Started);
        Assert.Equal("Rota da Nagisa", second.Fields.Notes);
        Assert.Equal(Now.AddHours(1), second.ModifiedAt);
        Assert.True(second.Pending);
    }

    [Fact]
    public void Editar_apaga_voto_e_notas_com_Clear_e_deixa_em_paz_o_que_nao_foi_pedido()
    {
        var first = LibraryListState.Edit(null, "v4", new VndbListUpdate { Vote = 85, Notes = "x", Finished = new DateOnly(2026, 3, 4) }, Now);

        var cleared = LibraryListState.Edit(first, "v4", new VndbListUpdate { Vote = VndbChange<int>.Clear, Notes = VndbChange<string>.Clear }, Now.AddHours(1));

        Assert.Null(cleared.Fields.Vote);
        Assert.Null(cleared.Fields.Notes);
        Assert.Equal(new DateOnly(2026, 3, 4), cleared.Fields.Finished);
    }

    [Theory]
    [InlineData(9, 1)]
    [InlineData(101, 1)]
    [InlineData(50, 7)]
    [InlineData(50, 0)]
    [InlineData(50, 1601)]
    public void Editar_recusa_voto_e_rotulo_invalidos(int vote, int label)
    {
        Assert.Throws<ArgumentException>(() => LibraryListState.Edit(null, "v4", new VndbListUpdate { Vote = vote, LabelsSet = [label] }, Now));
    }

    [Fact]
    public void Estado_vai_e_volta_pelo_library_json_sem_token_nem_segredo()
    {
        var state = LibraryListState.Edit(null, "v4", new VndbListUpdate { Vote = 85, LabelsSet = [2], Notes = "Ótimo", Finished = new DateOnly(2026, 3, 4) }, Now);
        var game = new LibraryGame("g1", "C:\\Jogos\\A", null, LibraryEngine.Unknown, "A", null, Now) { VndbId = "v4", List = state with { Base = LibraryListFields.Empty, SyncedAt = Now } };

        var json = LibraryFile.Serialize(new GameLibrary([game]));
        var back = LibraryFile.Parse(json).Games[0];

        Assert.Equal(85, back.CurrentList!.Fields.Vote);
        Assert.Equal([2], back.CurrentList.Fields.Labels.Select(l => l.Id));
        Assert.Equal(new DateOnly(2026, 3, 4), back.CurrentList.Fields.Finished);
        Assert.Equal("Ótimo", back.CurrentList.Fields.Notes);
        Assert.True(back.CurrentList.Pending);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Biblioteca_antiga_sem_lista_continua_abrindo()
    {
        var library = LibraryFile.Parse("""{"version":1,"games":[{"id":"g1","folder":"C:\\A","engine":{"id":"unknown","name":"Desconhecida","source":"unknown"},"title":"A","addedAt":"2026-11-01T12:00:00+00:00"}]}""");

        Assert.Null(library.Games[0].List);
        Assert.Null(library.Games[0].CurrentList);
    }
}
