using Microsoft.Extensions.Compliance.Classification;

namespace HouseholdApp.Application.Shared.Compliance;

// Classes of data that logs carry only in redacted form; the web host maps each class to a redactor.
public static class DataTaxonomy
{
    public const string Name = "HouseholdApp";

    public static DataClassification Secret { get; } = new(Name, nameof(Secret));

    public static DataClassification Private { get; } = new(Name, nameof(Private));
}

// Credentials: erased from logs.
public sealed class SecretDataAttribute() : DataClassificationAttribute(DataTaxonomy.Secret);

// What people typed or said, and names: pseudonymized in logs so entries still correlate.
public sealed class PrivateDataAttribute() : DataClassificationAttribute(DataTaxonomy.Private);
