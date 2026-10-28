using HouseholdApp.Application.Modules.Expenses.Application.Import;

namespace HouseholdApp.UnitTests.Modules.Expenses.Import;

public sealed class SplitwiseCsvParserTests
{
    private const string Header = "Data,Descrição,Categoria,Custo,Moeda,Ana Lima,Bruno Costa";

    private static string Csv(params string[] rows) =>
        $"{Header}\n\n{string.Join("\n", rows)}\n\n2026-01-31,Saldo total, , ,BRL,0.00,0.00\n\n";

    [Test]
    public async Task Parses_people_rows_and_reported_total()
    {
        var result = SplitwiseCsvParser.Parse(Csv(
            "2025-11-02,Mercado,Mercado,100.00,BRL,50.00,-50.00",
            "2025-11-03,Volta,Geral,100.00,BRL,-50.00,50.00"));

        await Assert.That(result.Errors.Count).IsEqualTo(0);
        var file = result.File!;
        await Assert.That(file.People.SequenceEqual(["Ana Lima", "Bruno Costa"])).IsTrue();
        await Assert.That(file.Rows.Count).IsEqualTo(2);
        await Assert.That(file.Rows[0].LineNumber).IsEqualTo(3);
        await Assert.That(file.Rows[0].Date).IsEqualTo(new DateOnly(2025, 11, 2));
        await Assert.That(file.Rows[0].Description).IsEqualTo("Mercado");
        await Assert.That(file.Rows[0].Kind).IsEqualTo(SplitwiseRowKind.Expense);
        await Assert.That(file.Rows[0].CostCents).IsEqualTo(10000L);
        await Assert.That(file.Rows[0].NetCents.SequenceEqual([5000L, -5000L])).IsTrue();
        await Assert.That(file.ReportedNetCents.SequenceEqual([0L, 0L])).IsTrue();
    }

    [Test]
    public async Task Quoted_description_keeps_commas_and_escaped_quotes()
    {
        var result = SplitwiseCsvParser.Parse(Csv(
            "2025-11-12,\"Pizza, \"\"grande\"\"\",Jantar fora,127.00,BRL,-63.50,63.50"));

        await Assert.That(result.File!.Rows[0].Description).IsEqualTo("Pizza, \"grande\"");
    }

    [Test]
    public async Task Byte_order_mark_and_crlf_are_accepted()
    {
        var text = "\uFEFF" + Csv("2025-11-02,Mercado,Mercado,100.00,BRL,50.00,-50.00").Replace("\n", "\r\n");

        var result = SplitwiseCsvParser.Parse(text);

        await Assert.That(result.Errors.Count).IsEqualTo(0);
        await Assert.That(result.File!.Rows.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("0.10", 10L)]
    [Arguments("0.01", 1L)]
    [Arguments("30.01", 3001L)]
    [Arguments("2154.33", 215433L)]
    [Arguments("2500", 250000L)]
    public async Task Converts_amounts_to_exact_centavos(string amount, long expected)
    {
        var result = SplitwiseCsvParser.Parse(Csv($"2025-11-02,X,Geral,{amount},BRL,0.00,0.00"));

        await Assert.That(result.File!.Rows[0].CostCents).IsEqualTo(expected);
    }

    [Test]
    public async Task Negative_nets_parse_to_negative_centavos()
    {
        var result = SplitwiseCsvParser.Parse(Csv("2025-11-02,X,Geral,209.00,BRL,-104.50,104.50"));

        await Assert.That(result.File!.Rows[0].NetCents.SequenceEqual([-10450L, 10450L])).IsTrue();
    }

    [Test]
    public async Task Pagamento_category_marks_a_payment()
    {
        var result = SplitwiseCsvParser.Parse(Csv("2025-11-28,Ana L. pagou Bruno C.,Pagamento,50.00,BRL,50.00,-50.00"));

        await Assert.That(result.File!.Rows[0].Kind).IsEqualTo(SplitwiseRowKind.Payment);
    }

    [Test]
    public async Task English_header_is_rejected()
    {
        var result = SplitwiseCsvParser.Parse("Date,Description,Category,Cost,Currency,A,B\n");

        await Assert.That(result.File).IsNull();
        await Assert.That(result.Errors[0].Code).IsEqualTo(ImportErrorCode.BadHeader);
    }

    [Test]
    public async Task Three_people_are_rejected()
    {
        var result = SplitwiseCsvParser.Parse(Header + ",Caio Souza\n");

        await Assert.That(result.Errors[0].Code).IsEqualTo(ImportErrorCode.BadHeader);
    }

    [Test]
    public async Task Other_currency_is_rejected_with_line_and_code()
    {
        var result = SplitwiseCsvParser.Parse(Csv("2025-11-02,Hotel,Geral,100.00,USD,50.00,-50.00"));

        await Assert.That(result.File).IsNull();
        await Assert.That(result.Errors[0].Code).IsEqualTo(ImportErrorCode.Currency);
        await Assert.That(result.Errors[0].LineNumber).IsEqualTo(3);
        await Assert.That(result.Errors[0].Detail).IsEqualTo("USD");
    }

    [Test]
    public async Task Bad_date_reports_its_line()
    {
        var result = SplitwiseCsvParser.Parse(Csv(
            "2025-11-02,Mercado,Mercado,100.00,BRL,50.00,-50.00",
            "02/11/2025,Mercado,Mercado,100.00,BRL,50.00,-50.00"));

        await Assert.That(result.Errors[0].Code).IsEqualTo(ImportErrorCode.BadDate);
        await Assert.That(result.Errors[0].LineNumber).IsEqualTo(4);
    }

    [Test]
    public async Task More_than_two_decimals_is_rejected()
    {
        var result = SplitwiseCsvParser.Parse(Csv("2025-11-02,X,Geral,10.005,BRL,0.00,0.00"));

        await Assert.That(result.Errors[0].Code).IsEqualTo(ImportErrorCode.TooManyDecimals);
    }

    [Test]
    public async Task Non_numeric_amount_is_rejected()
    {
        var result = SplitwiseCsvParser.Parse(Csv("2025-11-02,X,Geral,abc,BRL,0.00,0.00"));

        await Assert.That(result.Errors[0].Code).IsEqualTo(ImportErrorCode.BadAmount);
    }

    [Test]
    public async Task Missing_total_row_is_rejected()
    {
        var result = SplitwiseCsvParser.Parse($"{Header}\n\n2025-11-02,X,Geral,10.00,BRL,5.00,-5.00\n");

        await Assert.That(result.Errors.Any(e => e.Code == ImportErrorCode.MissingTotal)).IsTrue();
    }

    [Test]
    public async Task Empty_text_is_rejected()
    {
        var result = SplitwiseCsvParser.Parse("\n\n");

        await Assert.That(result.Errors[0].Code).IsEqualTo(ImportErrorCode.Empty);
    }

    [Test]
    public async Task Fictional_export_parses_completely()
    {
        var result = SplitwiseCsvParser.Parse(SplitwiseFixtures.Read(SplitwiseFixtures.Casa));

        await Assert.That(result.Errors.Count).IsEqualTo(0);
        await Assert.That(result.File!.Rows.Count).IsEqualTo(18);
        await Assert.That(result.File.ReportedNetCents.SequenceEqual([-65148L, 65148L])).IsTrue();
        await Assert.That(result.File.Rows.Count(r => r.Kind == SplitwiseRowKind.Payment)).IsEqualTo(3);
    }

    [Test]
    public async Task Amount_beyond_the_cap_is_rejected_without_throwing()
    {
        var result = SplitwiseCsvParser.Parse(Csv("2025-11-02,X,Geral,99999999999999999999,BRL,0.00,0.00"));

        await Assert.That(result.Errors[0].Code).IsEqualTo(ImportErrorCode.BadAmount);
    }

    [Test]
    public async Task Saldo_total_with_a_cost_is_an_ordinary_expense()
    {
        var result = SplitwiseCsvParser.Parse(Csv("2025-11-02,Saldo total,Geral,10.00,BRL,5.00,-5.00"));

        await Assert.That(result.Errors.Count).IsEqualTo(0);
        await Assert.That(result.File!.Rows.Single().Description).IsEqualTo("Saldo total");
    }

    [Test]
    public async Task Second_total_row_is_rejected()
    {
        var result = SplitwiseCsvParser.Parse(Csv(
            "2025-11-02,Mercado,Mercado,100.00,BRL,50.00,-50.00",
            "2026-01-31,Saldo total, , ,BRL,0.00,0.00"));

        await Assert.That(result.Errors.Any(e => e.Code == ImportErrorCode.DuplicateTotal)).IsTrue();
    }

    [Test]
    public async Task Quote_inside_an_unquoted_field_is_literal()
    {
        var result = SplitwiseCsvParser.Parse(Csv("2025-11-02,Tv 42\" nova,Geral,10.00,BRL,5.00,-5.00"));

        await Assert.That(result.Errors.Count).IsEqualTo(0);
        await Assert.That(result.File!.Rows[0].Description).IsEqualTo("Tv 42\" nova");
    }

    [Test]
    public async Task Line_numbers_follow_quoted_newlines()
    {
        var result = SplitwiseCsvParser.Parse(Csv(
            "2025-11-02,\"Linha 1\nLinha 2\",Geral,10.00,BRL,5.00,-5.00",
            "02/11/2025,X,Geral,10.00,BRL,5.00,-5.00"));

        await Assert.That(result.Errors[0].Code).IsEqualTo(ImportErrorCode.BadDate);
        await Assert.That(result.Errors[0].LineNumber).IsEqualTo(5);
    }

    [Test]
    public async Task Crlf_inside_quoted_description_becomes_lf()
    {
        var text = Csv("2025-11-02,\"A\nB\",Geral,10.00,BRL,5.00,-5.00").Replace("\n", "\r\n");

        var result = SplitwiseCsvParser.Parse(text);

        await Assert.That(result.File!.Rows[0].Description).IsEqualTo("A\nB");
    }

    [Test]
    public async Task Empty_person_name_is_rejected()
    {
        var result = SplitwiseCsvParser.Parse("Data,Descrição,Categoria,Custo,Moeda,Ana Lima, \n");

        await Assert.That(result.Errors[0].Code).IsEqualTo(ImportErrorCode.BadHeader);
    }
}
