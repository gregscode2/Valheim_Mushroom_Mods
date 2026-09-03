using Mono.Cecil;
var asm = AssemblyDefinition.ReadAssembly(@"F:\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll");
var method = asm.MainModule.Types.First(x => x.Name == "SpawnArea").Methods.First(m => m.Name == "Awake");
foreach (var i in method.Body.Instructions)
  Console.WriteLine(i.Offset.ToString("X4") + " " + i.OpCode + (i.Operand != null ? " " + i.Operand : ""));
