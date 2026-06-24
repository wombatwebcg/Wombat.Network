using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Wombat.Network.Mqtt.Generators;

[Generator]
public sealed class MqttPluginRegistrationGenerator : ISourceGenerator
{
    private const string PluginAttributeName = "Wombat.Network.Mqtt.Broker.MqttBrokerPluginAttribute";
    private const string PluginInterfaceName = "Wombat.Network.Mqtt.Broker.IMqttBrokerPlugin";

    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var pluginTypes = GetPluginTypes(context.Compilation);
        var source = new StringBuilder()
            .AppendLine("namespace Wombat.Network.Mqtt.Broker")
            .AppendLine("{")
            .AppendLine("    public static class GeneratedMqttPluginRegistrationExtensions")
            .AppendLine("    {")
            .AppendLine("        public static MqttBrokerOptions RegisterGeneratedPlugins(this MqttBrokerOptions options)")
            .AppendLine("        {")
            .AppendLine("            if (options == null)")
            .AppendLine("            {")
            .AppendLine("                throw new global::System.ArgumentNullException(nameof(options));")
            .AppendLine("            }");

        foreach (var pluginType in pluginTypes)
        {
            source.Append("            options.UsePlugin(new ")
                .Append(pluginType)
                .AppendLine("());");
        }

        var generatedSource = source
            .AppendLine("            return options;")
            .AppendLine("        }")
            .AppendLine("    }")
            .AppendLine("}")
            .ToString();

        context.AddSource("GeneratedMqttPluginRegistrationExtensions.g.cs", generatedSource);
    }

    private static string[] GetPluginTypes(Compilation compilation)
    {
        var plugins = new List<string>();
        CollectNamespacePluginTypes(compilation.Assembly.GlobalNamespace, plugins);
        return plugins.ToArray();

        void CollectNamespacePluginTypes(INamespaceSymbol namespaceSymbol, List<string> results)
        {
            foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                CollectNamespacePluginTypes(nestedNamespace, results);
            }

            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                CollectTypePluginTypes(type, results);
            }
        }

        void CollectTypePluginTypes(INamedTypeSymbol typeSymbol, List<string> results)
        {
            if (IsUsablePlugin(typeSymbol))
            {
                results.Add(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            foreach (var nestedType in typeSymbol.GetTypeMembers())
            {
                CollectTypePluginTypes(nestedType, results);
            }
        }

        bool IsUsablePlugin(INamedTypeSymbol typeSymbol)
        {
            if (typeSymbol.IsAbstract || typeSymbol.IsGenericType)
            {
                return false;
            }

            if (!typeSymbol.AllInterfaces.Any(x => x.ToDisplayString() == PluginInterfaceName))
            {
                return false;
            }

            if (!typeSymbol.GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() == PluginAttributeName))
            {
                return false;
            }

            return typeSymbol.Constructors.Any(x => x.Parameters.Length == 0 && x.DeclaredAccessibility == Accessibility.Public);
        }
    }
}
