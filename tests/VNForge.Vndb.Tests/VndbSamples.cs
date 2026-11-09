namespace VNForge.Vndb.Tests;

/// <summary>Respostas da API Kana no formato real, com jogos e personagens inventados.</summary>
internal static class VndbSamples
{
    public const string VisualNovels = """
        {"more":false,"results":[{
          "id":"v900001","title":"Tsuki no Hana","alttitle":"月の花",
          "titles":[{"lang":"ja","title":"月の花","latin":"Tsuki no Hana","official":true,"main":true},{"lang":"en","title":"Moon Flower","latin":null,"official":true,"main":false}],
          "aliases":["Tsukihana"],"olang":"ja","devstatus":0,"released":"2019-05-31",
          "languages":["en","ja"],"platforms":["win"],
          "image":{"url":"https://t.vndb.org/cv/00/1.jpg","thumbnail":"https://t.vndb.org/cv.t/00/1.jpg","dims":[800,1200],"sexual":0.5,"violence":0},
          "length":3,"length_minutes":1500,"length_votes":42,"rating":78.6,"description":"Uma [b]história[/b] de exemplo.",
          "tags":[{"id":"g1","name":"ADV","category":"tech","rating":2.5,"spoiler":0,"lie":false},{"id":"g2","name":"Twist","category":"cont","rating":1.2,"spoiler":2,"lie":true}]
        }]}
        """;

    public const string NoResults = """{"more":false,"results":[]}""";

    public const string Characters = """
        {"more":false,"results":[{
          "id":"c900001","name":"Hoshino Ami","original":"星野 亜美","aliases":["Ami"],"description":"A protagonista.",
          "image":{"url":"https://t.vndb.org/ch/00/1.jpg","dims":[256,300],"sexual":0,"violence":0},
          "gender":["f","m"],
          "vns":[{"id":"v900001","role":"main","spoiler":0,"release":null},{"id":"v900002","role":"side","spoiler":1,"release":{"id":"r900010"}}],
          "traits":[{"id":"i6","name":"Brown","group_id":"i1","group_name":"Hair","spoiler":0,"lie":false},{"id":"i2164","name":"Priest","group_id":"i40","group_name":"Role","spoiler":2,"lie":false}]
        }]}
        """;

    public const string CharactersFirstPage = """{"more":true,"results":[{"id":"c900001","name":"Primeiro"}]}""";

    public const string CharactersSecondPage = """{"more":false,"results":[{"id":"c900002","name":"Segundo"}]}""";

    public const string Releases = """
        {"more":false,"results":[{
          "id":"r900010","title":"Moon Flower","alttitle":"月の花","languages":[{"lang":"ja","title":"月の花","latin":"Tsuki no Hana","mtl":false,"main":true},{"lang":"pt-br","title":null,"latin":null,"mtl":true,"main":false}],
          "platforms":["win"],"released":"2019-05-31","minage":15,"patch":false,"freeware":false,"official":true,"engine":"YU-RIS","voiced":4,
          "vns":[{"id":"v900001","rtype":"complete"}]
        }]}
        """;

    public const string AuthInfo = """{"id":"u900","username":"alessandro","permissions":["listread","listwrite"]}""";

    public const string ListFirstPage = """
        {"more":true,"results":[{"id":"v900001","added":1700000000,"lastmod":1700000100,"voted":1700000200,"vote":80,"started":"2024-01-02","finished":null,"notes":"bom",
        "labels":[{"id":2,"label":"Finished"},{"id":7,"label":"Voted"}],"vn":{"title":"Moon Flower"}}]}
        """;

    public const string ListSecondPage = """
        {"more":false,"results":[{"id":"v900002","added":1700000300,"lastmod":1700000300,"voted":null,"vote":null,"started":null,"finished":null,"notes":null,"labels":[],"vn":{"title":"Outro"}}]}
        """;
}
