using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length < 2) throw new ArgumentException("Usage: CompatTools input.dll output.dll [Valheim directory]");
string gameRoot = args.Length > 2 ? Path.GetFullPath(args[2]) : @"C:\Program Files (x86)\Steam\steamapps\common\Valheim";
string game = Path.Combine(gameRoot, "valheim_Data", "Managed");
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(game);
resolver.AddSearchDirectory(Path.Combine(gameRoot, "BepInEx", "core"));
resolver.AddSearchDirectory(Path.Combine(gameRoot, "BepInEx", "plugins", "Jotunn"));
string source = args[0];
using var assembly = AssemblyDefinition.ReadAssembly(source, new ReaderParameters { AssemblyResolver = resolver });
var module = assembly.MainModule;
var gameModule = AssemblyDefinition.ReadAssembly(Path.Combine(game, "assembly_valheim.dll"));
var typeRemaps = new Dictionary<string, string> { ["InventoryGrid/Element"] = "InventoryElement" };
foreach (var reference in module.GetTypeReferences())
{
    if (typeRemaps.TryGetValue(reference.FullName, out var replacement))
    {
        Console.WriteLine("TYPE " + reference.FullName + " -> " + replacement);
        reference.DeclaringType = null;
        reference.Namespace = "";
        reference.Name = replacement;
        reference.Scope = module.ImportReference(gameModule.MainModule.GetType(replacement)).Scope;
    }
}
IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types) { yield return type; foreach (var child in AllTypes(type.NestedTypes)) yield return child; }
}
bool Relevant(TypeReference type) => type.Scope?.Name is "assembly_valheim" or "assembly_utils" or "assembly_guiutils";
var unresolved = new HashSet<string>();
int changed = 0;
// Harmony's overload selectors contain old parameter arrays too.
foreach (var type in AllTypes(module.Types))
foreach (var provider in new ICustomAttributeProvider[] { type }.Concat(type.Methods))
{
    var context = new List<CustomAttribute>();
    for (TypeDefinition parent = type; parent != null; parent = parent.DeclaringType)
        context.AddRange(parent.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"));
    if (provider != type) context.AddRange(provider.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"));
    var target = context.SelectMany(a => a.ConstructorArguments).Where(a => a.Type.FullName == "System.Type").Select(a => a.Value as TypeReference).LastOrDefault();
    var name = context.SelectMany(a => a.ConstructorArguments).Where(a => a.Type.FullName == "System.String").Select(a => a.Value as string).LastOrDefault();
    if (target == null || name == null || !Relevant(target)) continue;
    foreach (var attr in provider.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
    for (int i = 0; i < attr.ConstructorArguments.Count; i++)
    {
        var arg = attr.ConstructorArguments[i];
        if (arg.Type.FullName != "System.Type[]") continue;
        var old = ((CustomAttributeArgument[])arg.Value).Select(a => ((TypeReference)a.Value).FullName).ToArray();
        var methods = target.Resolve().Methods.Where(m => m.Name == name).ToArray();
        if (methods.Any(m => m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(old))) continue;
        var candidates = methods.Where(m => m.Parameters.Count > old.Length && m.Parameters.Take(old.Length).Select(p => p.ParameterType.FullName).SequenceEqual(old) && m.Parameters.Skip(old.Length).All(p => p.IsOptional)).ToArray();
        if (candidates.Length != 1) { unresolved.Add("HARMONY " + target.FullName + "::" + name + "(" + string.Join(",", old) + ") candidates=" + string.Join(";", methods.Select(m => m.FullName))); continue; }
        var sysType = ((ArrayType)arg.Type).ElementType;
        attr.ConstructorArguments[i] = new CustomAttributeArgument(arg.Type, candidates[0].Parameters.Select(p => new CustomAttributeArgument(sysType, module.ImportReference(p.ParameterType))).ToArray());
        Console.WriteLine("HARMONY " + candidates[0].FullName);
        changed++;
    }
}
foreach (var type in AllTypes(module.Types))
foreach (var method in type.Methods.Where(m => m.HasBody))
foreach (var instruction in method.Body.Instructions.ToArray())
{
    if (instruction.Operand is FieldReference field && Relevant(field.DeclaringType))
    {
        if (field.DeclaringType.FullName == "InventoryElement" && field.Name == "m_go" && instruction.OpCode == OpCodes.Ldfld)
        {
            using var unity = AssemblyDefinition.ReadAssembly(Path.Combine(game, "UnityEngine.CoreModule.dll"));
            var getter = unity.MainModule.GetType("UnityEngine.Component").Methods.Single(m => m.Name == "get_gameObject");
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = module.ImportReference(getter);
            Console.WriteLine("FIELD-TO-PROPERTY InventoryElement.m_go -> Component.gameObject");
            changed++;
            continue;
        }
        FieldDefinition definition = null;
        try { definition = field.Resolve(); } catch { }
        if (definition == null) { unresolved.Add("FIELD " + field.FullName); continue; }
        if (definition.IsLiteral && instruction.OpCode == OpCodes.Ldsfld)
        {
            Console.WriteLine("CONST " + field.FullName + " = " + definition.Constant);
            switch (definition.Constant)
            {
                case long v: instruction.OpCode = OpCodes.Ldc_I8; instruction.Operand = v; break;
                case int v: instruction.OpCode = OpCodes.Ldc_I4; instruction.Operand = v; break;
                case bool v: instruction.OpCode = OpCodes.Ldc_I4; instruction.Operand = v ? 1 : 0; break;
                default: throw new Exception("Unsupported constant " + field.FullName);
            }
            changed++;
        }
    }
    if (instruction.Operand is not MethodReference call || !Relevant(call.DeclaringType)) continue;
    MethodDefinition existing = null;
    try { existing = call.Resolve(); } catch { }
    if (existing != null) continue;
    TypeDefinition declaring = null;
    try { declaring = call.DeclaringType.Resolve(); } catch { }
    var candidates = declaring?.Methods.Where(m => m.Name == call.Name && m.HasThis == call.HasThis
        && m.ReturnType.FullName == call.ReturnType.FullName && m.Parameters.Count > call.Parameters.Count
        && m.Parameters.Take(call.Parameters.Count).Select(p => p.ParameterType.FullName).SequenceEqual(call.Parameters.Select(p => p.ParameterType.FullName))
        && m.Parameters.Skip(call.Parameters.Count).All(p => p.IsOptional)).ToArray() ?? Array.Empty<MethodDefinition>();
    if (candidates.Length != 1 || (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt))
    { unresolved.Add("METHOD " + call.FullName); continue; }
    var replacement = candidates[0];
    Console.WriteLine("CALL " + call.FullName + " -> " + replacement.FullName);
    var extras = new List<Instruction>();
    foreach (var parameter in replacement.Parameters.Skip(call.Parameters.Count))
    {
        object value = parameter.HasConstant ? parameter.Constant : null;
        if (value == null && parameter.ParameterType.IsValueType)
        {
            var defaultValue = new VariableDefinition(module.ImportReference(parameter.ParameterType));
            method.Body.Variables.Add(defaultValue);
            method.Body.InitLocals = true;
            extras.Add(Instruction.Create(OpCodes.Ldloca, defaultValue));
            extras.Add(Instruction.Create(OpCodes.Initobj, defaultValue.VariableType));
            extras.Add(Instruction.Create(OpCodes.Ldloc, defaultValue));
            continue;
        }
        extras.Add(value switch {
            bool b => Instruction.Create(OpCodes.Ldc_I4, b ? 1 : 0),
            int n => Instruction.Create(OpCodes.Ldc_I4, n),
            short n => Instruction.Create(OpCodes.Ldc_I4, (int)n),
            float f => Instruction.Create(OpCodes.Ldc_R4, f),
            long n => Instruction.Create(OpCodes.Ldc_I8, n),
            string s => Instruction.Create(OpCodes.Ldstr, s),
            null => Instruction.Create(OpCodes.Ldnull),
            _ => throw new Exception("Unsupported default " + parameter.Name)
        });
    }
    var finalCall = Instruction.Create(instruction.OpCode, module.ImportReference(replacement));
    // Keep the original instruction as the first default push so branches and
    // exception boundaries targeting this call still execute the full sequence.
    instruction.OpCode = extras[0].OpCode;
    instruction.Operand = extras[0].Operand;
    var previous = instruction;
    var il = method.Body.GetILProcessor();
    foreach (var next in extras.Skip(1).Append(finalCall)) { il.InsertAfter(previous, next); previous = next; }
    changed++;
}
foreach (var reference in module.GetTypeReferences().Where(Relevant))
{
    try { if (reference.Resolve() == null) unresolved.Add("TYPE " + reference.FullName); }
    catch { unresolved.Add("TYPE " + reference.FullName); }
}
if (assembly.Name.Name == "Recycle_N_Reclaim")
{
    // ObjectDB in 1.0 can contain entries without ItemDrop. Ignore them while
    // locating reclaim ingredients instead of dereferencing a missing component.
    var predicate = AllTypes(module.Types).SelectMany(t => t.Methods).Single(m => m.Name.Contains("<AnalyzeMaterialYieldForItem>") && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "UnityEngine.GameObject");
    var getComponent = (MethodReference)predicate.Body.Instructions.Single(i => i.Operand is GenericInstanceMethod g && g.Name == "GetComponent" && g.GenericArguments[0].FullName == "ItemDrop").Operand;
    using var unity = AssemblyDefinition.ReadAssembly(Path.Combine(game, "UnityEngine.CoreModule.dll"));
    var isAlive = module.ImportReference(unity.MainModule.GetType("UnityEngine.Object").Methods.Single(m => m.Name == "op_Implicit"));
    var first = predicate.Body.Instructions[0];
    var reject = Instruction.Create(OpCodes.Ldc_I4_0);
    var guards = new[] {
        Instruction.Create(OpCodes.Ldarg, predicate.Parameters[0]), Instruction.Create(OpCodes.Call, isAlive), Instruction.Create(OpCodes.Brfalse, reject),
        Instruction.Create(OpCodes.Ldarg, predicate.Parameters[0]), Instruction.Create(OpCodes.Callvirt, getComponent), Instruction.Create(OpCodes.Call, isAlive), Instruction.Create(OpCodes.Brtrue, first),
        reject, Instruction.Create(OpCodes.Ret)
    };
    foreach (var guard in guards) predicate.Body.GetILProcessor().InsertBefore(first, guard);
    Console.WriteLine("GUARD reclaim ingredient lookup skips null/non-item prefabs");
    changed++;
}
Console.WriteLine("CHANGES: " + changed);
foreach (var entry in unresolved.Order()) Console.WriteLine("UNRESOLVED " + entry);
if (unresolved.Count > 0) throw new InvalidOperationException("Unresolved references; refusing to write a partial patch.");
if (args.Length > 1)
{
    var opcodes = typeof(OpCodes).GetFields().Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Name);
    foreach (var type in AllTypes(module.Types))
    foreach (var method in type.Methods.Where(m => m.HasBody))
    foreach (var instruction in method.Body.Instructions)
        if (instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget)
            instruction.OpCode = opcodes[instruction.OpCode.Name[..^2]];
    assembly.Write(args[1]);
    Console.WriteLine("WROTE " + args[1]);
}
