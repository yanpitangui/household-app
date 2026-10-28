using System.Globalization;
using System.Text;

namespace HouseholdApp.Application.Modules.Expenses.Application.Import;

/// <summary>
/// Reads a Splitwise group export ("Export as spreadsheet", pt-BR locale): fixed columns
/// Data,Descrição,Categoria,Custo,Moeda followed by one net-amount column per person, and a
/// closing "Saldo total" row with Splitwise's own balance.
/// </summary>
public static class SplitwiseCsvParser
{
    private static readonly string[] FixedColumns = ["Data", "Descrição", "Categoria", "Custo", "Moeda"];
    private const int PersonColumns = 2;
    private const string PaymentCategory = "Pagamento";
    private const string TotalDescription = "Saldo total";
    private const string SupportedCurrency = "BRL";
    private const decimal MaxAmount = 1_000_000_000m;

    public static ParseResult Parse(string text)
    {
        var records = ReadRecords(text.TrimStart('\uFEFF'))
            .Where(r => r.Fields.Count > 1 || r.Fields[0].Trim().Length > 0)
            .ToList();
        if (records.Count == 0) return new ParseResult(null, [new ImportError(ImportErrorCode.Empty)]);

        var header = records[0];
        if (header.Fields.Count != FixedColumns.Length + PersonColumns
            || !header.Fields.Take(FixedColumns.Length).Select(f => f.Trim()).SequenceEqual(FixedColumns))
            return new ParseResult(null, [new ImportError(ImportErrorCode.BadHeader, header.Line)]);

        var people = header.Fields.Skip(FixedColumns.Length).Select(f => f.Trim()).ToList();
        if (people.Any(p => p.Length == 0))
            return new ParseResult(null, [new ImportError(ImportErrorCode.BadHeader, header.Line)]);

        var rows = new List<SplitwiseRow>();
        var errors = new List<ImportError>();
        long[]? reported = null;

        foreach (var record in records.Skip(1))
        {
            var f = record.Fields;
            if (f.Count != header.Fields.Count)
            {
                errors.Add(new ImportError(ImportErrorCode.BadColumnCount, record.Line));
                continue;
            }

            var currency = f[4].Trim();
            if (currency != SupportedCurrency)
            {
                errors.Add(new ImportError(ImportErrorCode.Currency, record.Line, currency));
                continue;
            }

            var nets = new long[PersonColumns];
            var netErrors = new List<ImportError>();
            for (var i = 0; i < PersonColumns; i++)
            {
                var netError = TryParseCents(f[FixedColumns.Length + i], record.Line, out nets[i]);
                if (netError is not null) netErrors.Add(netError);
            }
            if (netErrors.Count > 0)
            {
                errors.AddRange(netErrors);
                continue;
            }

            var description = f[1].Trim();
            if (description == TotalDescription && f[3].Trim().Length == 0)
            {
                if (reported is not null)
                {
                    errors.Add(new ImportError(ImportErrorCode.DuplicateTotal, record.Line));
                    continue;
                }
                reported = nets;
                continue;
            }

            if (!DateOnly.TryParseExact(f[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                errors.Add(new ImportError(ImportErrorCode.BadDate, record.Line, f[0].Trim()));
                continue;
            }

            var costError = TryParseCents(f[3], record.Line, out var cost);
            if (costError is not null)
            {
                errors.Add(costError);
                continue;
            }

            var kind = f[2].Trim() == PaymentCategory ? SplitwiseRowKind.Payment : SplitwiseRowKind.Expense;
            rows.Add(new SplitwiseRow(record.Line, date, description, kind, cost, nets));
        }

        if (reported is null) errors.Add(new ImportError(ImportErrorCode.MissingTotal));
        if (rows.Count == 0 && errors.Count == 0) errors.Add(new ImportError(ImportErrorCode.NoRows));

        return errors.Count > 0
            ? new ParseResult(null, errors)
            : new ParseResult(new ParsedSplitwiseFile(people, rows, reported!), []);
    }

    private static ImportError? TryParseCents(string raw, int line, out long cents)
    {
        cents = 0;
        var value = raw.Trim();
        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount))
            return new ImportError(ImportErrorCode.BadAmount, line, value);
        if (Math.Abs(amount) > MaxAmount)
            return new ImportError(ImportErrorCode.BadAmount, line, value);
        if (decimal.Round(amount, 2) != amount)
            return new ImportError(ImportErrorCode.TooManyDecimals, line, value);
        cents = (long)(amount * 100);
        return null;
    }

    private sealed record CsvRecord(int Line, List<string> Fields);

    // RFC 4180: quoted fields may contain commas, newlines and "" for a literal quote.
    private static IEnumerable<CsvRecord> ReadRecords(string text)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else inQuotes = false;
                }
                else if (ch != '\r')
                {
                    if (ch == '\n') line++;
                    field.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    yield return new CsvRecord(recordLine, fields);
                    fields = [];
                    line++;
                    recordLine = line;
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return new CsvRecord(recordLine, fields);
        }
    }
}
