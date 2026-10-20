using System.Collections.ObjectModel;

namespace PdfForm.Publication;

/// <summary>Configuration assessments never imply data, packet or rendering validation.</summary>
public static class ConfigurationPublicationStages
{
    public static IReadOnlyDictionary<string, string> Create(string syntax, string profile,
        string binding, string bindingStructure)
    {
        foreach (var state in new[] { syntax, profile, binding, bindingStructure })
            if (state is not ("passed" or "failed" or "not_run"))
                throw new ArgumentException("E_CONFIGURATION_VALIDATION_STATE");
        return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["syntax"] = syntax, ["profile"] = profile, ["binding"] = binding,
            ["bindingStructure"] = bindingStructure, ["data"] = "not_run",
            ["packet"] = "not_run", ["render"] = "not_run"
        });
    }
}
