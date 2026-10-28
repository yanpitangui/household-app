namespace HouseholdApp.Application.Modules.Expenses.Application.Import;

/// <summary>
/// Turns parsed rows into expenses and settlements by person column. With two people, the column
/// with the positive net paid the full cost and the other column owes it that net, which recovers
/// the exact funding and allocation from Splitwise's net-only export.
/// </summary>
public static class SplitwiseImportPlanner
{
    public static PlanResult Build(ParsedSplitwiseFile file)
    {
        var expenses = new List<PlannedExpense>();
        var settlements = new List<PlannedSettlement>();
        var errors = new List<ImportError>();

        foreach (var row in file.Rows)
        {
            var nets = row.NetCents;
            if (nets[0] + nets[1] != 0)
            {
                errors.Add(new ImportError(ImportErrorCode.RowNotBalanced, row.LineNumber));
                continue;
            }
            if (nets[0] == 0)
            {
                errors.Add(new ImportError(ImportErrorCode.ZeroRow, row.LineNumber));
                continue;
            }

            var payer = nets[0] > 0 ? 0 : 1;
            var owedToPayer = nets[payer];

            if (row.Kind == SplitwiseRowKind.Payment)
            {
                if (owedToPayer != row.CostCents)
                {
                    errors.Add(new ImportError(ImportErrorCode.PaymentMismatch, row.LineNumber));
                    continue;
                }
                settlements.Add(new PlannedSettlement(row.LineNumber, row.Date, payer, owedToPayer));
                continue;
            }

            if (owedToPayer > row.CostCents)
            {
                errors.Add(new ImportError(ImportErrorCode.ShareExceedsCost, row.LineNumber));
                continue;
            }

            var payerShare = row.CostCents - owedToPayer;
            expenses.Add(new PlannedExpense(
                row.LineNumber, row.Date, row.Description, payer, row.CostCents, payerShare, owedToPayer));
        }

        if (errors.Count > 0) return new PlanResult(null, errors);

        var computed = new long[file.People.Count];
        foreach (var e in expenses)
        {
            // Each column's net is what it paid minus its own share.
            computed[e.PayerColumn] += e.CostCents - e.PayerShareCents;
            computed[1 - e.PayerColumn] -= e.OtherShareCents;
        }
        foreach (var s in settlements)
        {
            computed[s.PayerColumn] += s.Cents;
            computed[1 - s.PayerColumn] -= s.Cents;
        }

        var plan = new ImportPlan(file.People, expenses, settlements, computed, file.ReportedNetCents);
        return plan.IsBalanced
            ? new PlanResult(plan, [])
            : new PlanResult(plan, [new ImportError(ImportErrorCode.TotalMismatch)]);
    }

    public static IReadOnlyList<ImportError> ValidateMapping(
        IReadOnlyList<Guid?> memberForColumn, IReadOnlySet<Guid> householdMemberIds)
    {
        if (memberForColumn.Count != 2 || memberForColumn.Any(m => m is null))
            return [new ImportError(ImportErrorCode.Unmapped)];
        if (memberForColumn[0] == memberForColumn[1])
            return [new ImportError(ImportErrorCode.DuplicateMapping)];
        if (memberForColumn.Any(m => !householdMemberIds.Contains(m!.Value)))
            return [new ImportError(ImportErrorCode.NotAMember)];
        return [];
    }
}
