using System.Text.Json;

namespace ApiCompat;

internal static class Program
{
    private static int Main(string[] args)
    {
        List<MetadataAssembly> opened = [];
        try
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine("ApiCompat --mod MOD.dll --reference DEPENDENCY.dll [--reference ...] [--scope ASSEMBLY_OR_PREFIX*]");
                Console.WriteLine("Defaults: --scope sts2 --scope STS2-RitsuLib*. Reads PE metadata only; never loads or executes the assemblies.");
                return args.Length == 0 ? 2 : 0;
            }
            string? modPath = null;
            List<string> referencePaths = [], scopes = [];
            for (int index = 0; index < args.Length; index++)
            {
                string option = args[index];
                if (++index >= args.Length) throw new ArgumentException("Missing value for " + option);
                switch (option)
                {
                    case "--mod" when modPath is null: modPath = args[index]; break;
                    case "--reference": referencePaths.Add(args[index]); break;
                    case "--scope": scopes.Add(args[index]); break;
                    default: throw new ArgumentException("Unknown or repeated option: " + option);
                }
            }
            if (modPath is null || referencePaths.Count == 0) throw new ArgumentException("--mod and --reference are required.");
            var mod = new MetadataAssembly(modPath);
            opened.Add(mod);
            foreach (var path in referencePaths.Distinct(StringComparer.OrdinalIgnoreCase)) opened.Add(new MetadataAssembly(path));
            var report = new CompatibilityCheck(opened.Skip(1), scopes.Count == 0 ? ["sts2", "STS2-RitsuLib*"] : scopes.ToArray()).Check(mod);
            if (report.CheckedTypes == 0 && report.CheckedMembers == 0)
                throw new ArgumentException("No references matched the selected scopes; refusing an empty compatibility check.");
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            }));
            return report.Status == "passed" ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("API compatibility check failed: " + error.Message);
            return 2;
        }
        finally { foreach (var assembly in opened) assembly.Dispose(); }
    }
}
