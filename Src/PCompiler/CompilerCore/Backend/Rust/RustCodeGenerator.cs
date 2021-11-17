using Plang.Compiler.Backend.ASTExt;
using Plang.Compiler.TypeChecker;
using Plang.Compiler.TypeChecker.AST;
using Plang.Compiler.TypeChecker.AST.Declarations;
using Plang.Compiler.TypeChecker.AST.Expressions;
using Plang.Compiler.TypeChecker.AST.Statements;
using Plang.Compiler.TypeChecker.AST.States;
using Plang.Compiler.TypeChecker.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Plang.Compiler.Backend.Rust
{
    public class RustCodeGenerator : ICodeGenerator
    {
        public IEnumerable<CompiledFile> GenerateCode(ICompilationJob job, Scope globalScope)
        {
            CompilationContext context = new CompilationContext(job);
            CompiledFile rustSource = GenerateSource(context, globalScope);
            return new List<CompiledFile> { rustSource };
        }

        private static string EventTypeName = "ProtocolEvent";
        private static string EventNameTypeName = "ProtocolEventName";
        private static string DefaultEventName = "DefaultEvent";

        private static string EventTypeDecl = $@"#[derive(Debug)]
pub struct {EventTypeName} {{
    pub name: {EventNameTypeName},
    pub payload: PV::PValue,
}}";

        private static string EventTraitDecl = @$"impl EV::PEvent for {EventTypeName} {{
    fn default() -> Self {{
        {EventTypeName} {{
            name: {EventNameTypeName}::{DefaultEventName},
            payload: PV::PValue::DefaultVal
        }}
    }}
}}";

        private void WriteEvents(CompilationContext context, StringWriter output,
            IEnumerable<PEvent> pEvents)
        {
            context.WriteLine(output, "");
            context.WriteLine(output, "#[derive(Debug)]");
            context.WriteLine(output, $"pub enum {EventNameTypeName}");
            context.WriteLine(output, "{");
            foreach (PEvent pEvent in pEvents)
            {
                string declName = context.Names.GetNameForDecl(pEvent);
                context.WriteLine(output, $"{declName},");
            }
            context.WriteLine(output, "}");
            context.WriteLine(output, "");

            context.WriteLine(output, EventTypeDecl);
            context.WriteLine(output, "");
            context.WriteLine(output, EventTraitDecl);
        }

        private CompiledFile GenerateSource(CompilationContext context, Scope globalScope)
        {
            CompiledFile source = new CompiledFile(context.FileName);

            WriteSourcePrologue(context, source.Stream);

            // Rust Code Begins
            WriteEvents(context, source.Stream, globalScope.Events);
            // Rust Code Ends

            // write the top level declarations
            foreach (IPDecl decl in globalScope.AllDecls)
            {
                WriteDecl(context, source.Stream, decl);
            }

            // write the machine creation function
            WriteMachineCreationFunction(context, source.Stream, globalScope);

            // write the interface declarations
            //WriteInitializeInterfaces(context, source.Stream, globalScope.Interfaces);

            // write the enum declarations
            //WriteInitializeEnums(context, source.Stream, globalScope.Enums);

            //WriteSourceEpilogue(context, source.Stream);

            return source;
        }

        private static string MachineCreationFunDeclPrologue = $@"
pub fn create_new_machine(name: &'static str, args: PV::PValue) -> MP::Machine<{EventTypeName}> {{
    let(tx_config_to_machine, rx_config_to_machine) = mpsc::channel();
    let(tx_machine_to_config, rx_machine_to_config) = mpsc::channel();

    let machine_handle = thread::spawn(move || {{
        let mut state_machine: Box < dyn PStateMachine < Event = {EventTypeName} >>;";

        private static string MachineCreationFunDeclEpilogue = @"

        state_machine.add_to_config();

        state_machine.answer_requests();
    });

    MP::Machine {
        thread_handle: machine_handle,
        tx_config_to_machine,
        rx_machine_to_config,
        execute_status: MP::ExecutionStatus::CanExecuteFurther,
    }
}";

        private void WriteMachineCreationFunction(CompilationContext context, StringWriter output, Scope globalScope)
        {
            context.WriteLine(output, MachineCreationFunDeclPrologue);

            context.WriteLine(output, "match name {");

            foreach (IPDecl decl in globalScope.AllDecls)
            {
                switch (decl)
                {
                    case Machine machine:
                        string machine_name = context.Names.GetNameForDecl(machine);

                        context.WriteLine(output, $"\"{machine_name}\" => {{");
                        context.Write(output, $"state_machine = ");
                        context.WriteLine(output, $"{machine_name}::new(tx_machine_to_config, rx_config_to_machine, args)");
                        context.WriteLine(output, "}");
                        break;

                    default:
                        break;
                }
            }

            context.WriteLine(output, "_ => panic!(\"Unrecognized machine name {}\", name),");

            context.WriteLine(output, "}");

            context.WriteLine(output, MachineCreationFunDeclEpilogue);
        }

        private void WriteInitializeInterfaces(CompilationContext context, StringWriter output,
            IEnumerable<Interface> interfaces)
        {
            WriteNameSpacePrologue(context, output);

            //create the interface declarations
            List<Interface> ifaces = interfaces.ToList();
            foreach (Interface iface in ifaces)
            {
                context.WriteLine(output, $"public class {context.Names.GetNameForDecl(iface)} : PMachineValue {{");
                context.WriteLine(output,
                    $"public {context.Names.GetNameForDecl(iface)} (ActorId machine, List<string> permissions) : base(machine, permissions) {{ }}");
                context.WriteLine(output, "}");
                context.WriteLine(output);
            }

            //initialize the interfaces
            context.WriteLine(output, "public partial class PHelper {");
            context.WriteLine(output, "public static void InitializeInterfaces() {");
            context.WriteLine(output, "PInterfaces.Clear();");
            foreach (Interface iface in ifaces)
            {
                context.Write(output, $"PInterfaces.AddInterface(nameof({context.Names.GetNameForDecl(iface)})");
                foreach (PEvent ev in iface.ReceivableEvents.Events)
                {
                    context.Write(output, ", ");
                    context.Write(output, $"nameof({context.Names.GetNameForDecl(ev)})");
                }

                context.WriteLine(output, ");");
            }

            context.WriteLine(output, "}");
            context.WriteLine(output, "}");
            context.WriteLine(output);

            WriteNameSpaceEpilogue(context, output);
        }

        private void WriteSourcePrologue(CompilationContext context, StringWriter output)
        {
            context.WriteLine(output, "/*");
            context.WriteLine(output, "using Microsoft.Coyote;");
            context.WriteLine(output, "using Microsoft.Coyote.Actors;");
            context.WriteLine(output, "using Microsoft.Coyote.Runtime;");
            context.WriteLine(output, "using Microsoft.Coyote.Specifications;");
            context.WriteLine(output, "using System;");
            context.WriteLine(output, "using System.Runtime;");
            context.WriteLine(output, "using System.Collections.Generic;");
            context.WriteLine(output, "using System.Linq;");
            context.WriteLine(output, "using System.IO;");
            context.WriteLine(output, "using Plang.CSharpRuntime;");
            context.WriteLine(output, "using Plang.CSharpRuntime.Values;");
            context.WriteLine(output, "using Plang.CSharpRuntime.Exceptions;");
            context.WriteLine(output, "using System.Threading;");
            context.WriteLine(output, "using System.Threading.Tasks;");
            context.WriteLine(output);
            context.WriteLine(output, "#pragma warning disable 162, 219, 414, 1998");
            context.WriteLine(output, $"namespace PImplementation");
            context.WriteLine(output, "{");
            context.WriteLine(output, "}");
            context.WriteLine(output, "*/");
            // Rust Code Begins
            context.WriteLine(output, "use amzn_p_rust::common_machine_data as MD;");
            context.WriteLine(output, "use amzn_p_rust::machine_index as M;");
            context.WriteLine(output, "use amzn_p_rust::message_passing as MP;");
            context.WriteLine(output, "use amzn_p_rust::p_state_machine::PStateMachine;");
            context.WriteLine(output, "use amzn_p_rust::p_value as PV;");
            context.WriteLine(output, "use amzn_p_rust::p_event as EV;");
            context.WriteLine(output, "use amzn_p_rust::p_value::Clonable;");
            context.WriteLine(output, "");
            context.WriteLine(output, "use std::collections::HashMap;");
            context.WriteLine(output, "use std::sync::mpsc;");
            context.WriteLine(output, "use std::thread;");
            // Rust Code Ends
        }

        private void WriteSourceEpilogue(CompilationContext context, StringWriter output)
        {
            context.WriteLine(output, "/*");
            context.WriteLine(output, "#pragma warning restore 162, 219, 414");
            context.WriteLine(output, "*/");
        }

        private void WriteNameSpacePrologue(CompilationContext context, StringWriter output)
        {
            context.WriteLine(output, $"namespace PImplementation");
            context.WriteLine(output, "{");
        }

        private void WriteNameSpaceEpilogue(CompilationContext context, StringWriter output)
        {
            context.WriteLine(output, "}");
        }

        private void WriteDecl(CompilationContext context, StringWriter output, IPDecl decl)
        {
            string declName;
            switch (decl)
            {
                case Function function:
                    if (!function.IsForeign)
                    {
                        context.WriteLine(output, $"namespace PImplementation");
                        context.WriteLine(output, "{");
                        context.WriteLine(output, $"public static partial class {context.GlobalFunctionClassName}");
                        context.WriteLine(output, "{");
                        WriteFunction(context, output, function);
                        context.WriteLine(output, "}");
                        context.WriteLine(output, "}");
                    }

                    break;

                case PEvent pEvent:
                    if (!pEvent.IsBuiltIn)
                    {
                        context.WriteLine(output, "/*");
                        WriteEvent(context, output, pEvent);
                        context.WriteLine(output, "*/");
                    }

                    break;

                case Machine machine:
                    if (machine.IsSpec)
                    {
                        WriteMonitor(context, output, machine);
                    }
                    else
                    {
                        WriteMachine(context, output, machine);
                    }

                    break;

                case PEnum _:
                    break;

                case TypeDef typeDef:
                    context.WriteLine(output, "/*");
                    ForeignType foreignType = typeDef.Type as ForeignType;
                    if (foreignType != null)
                    {
                        WriteForeignType(context, output, foreignType);
                    }
                    context.WriteLine(output, "*/");

                    break;

                case Implementation impl:
                    context.WriteLine(output, "/*");
                    WriteImplementationDecl(context, output, impl);
                    context.WriteLine(output, "*/");
                    break;

                case SafetyTest safety:
                    context.WriteLine(output, "/*");
                    WriteSafetyTestDecl(context, output, safety);
                    context.WriteLine(output, "*/");
                    break;

                case Interface _:
                    break;

                case EnumElem _:
                    break;

                default:
                    declName = context.Names.GetNameForDecl(decl);
                    context.WriteLine(output, $"// TODO: {decl.GetType().Name} {declName}");
                    break;
            }
        }

        private void WriteMonitor(CompilationContext context, StringWriter output, Machine machine)
        {
            WriteNameSpacePrologue(context, output);

            string declName = context.Names.GetNameForDecl(machine);
            context.WriteLine(output, $"internal partial class {declName} : PMonitor");
            context.WriteLine(output, "{");

            foreach (Variable field in machine.Fields)
            {
                context.WriteLine(output,
                    $"private {GetCSharpType(field.Type)} {context.Names.GetNameForDecl(field)} = {DefaultValueForType(field.Type)};");
            }

            WriteMonitorConstructor(context, output, machine);

            foreach (Function method in machine.Methods)
            {
                WriteFunction(context, output, method);
            }

            foreach (State state in machine.States)
            {
                WriteState(context, output, state);
            }

            context.WriteLine(output, "}");

            WriteNameSpaceEpilogue(context, output);
        }

        private void WriteMonitorConstructor(CompilationContext context, StringWriter output, Machine machine)
        {
            string declName = context.Names.GetNameForDecl(machine);
            context.WriteLine(output, $"static {declName}() {{");
            foreach (PEvent sEvent in machine.Observes.Events)
            {
                context.WriteLine(output, $"observes.Add(nameof({context.Names.GetNameForDecl(sEvent)}));");
            }

            context.WriteLine(output, "}");
            context.WriteLine(output);
        }

        private static void WriteForeignType(CompilationContext context, StringWriter output, ForeignType foreignType)
        {
            // we do not generate code for foreign types
            string declName = foreignType.CanonicalRepresentation;
            context.WriteLine(output, $"// TODO: Implement the Foreign Type {declName}");
        }

        private void WriteSafetyTestDecl(CompilationContext context, StringWriter output, SafetyTest safety)
        {
            WriteNameSpacePrologue(context, output);

            context.WriteLine(output, $"public class {context.Names.GetNameForDecl(safety)} {{");
            WriteInitializeLinkMap(context, output, safety.ModExpr.ModuleInfo.LinkMap);
            WriteInitializeInterfaceDefMap(context, output, safety.ModExpr.ModuleInfo.InterfaceDef);
            WriteInitializeMonitorObserves(context, output, safety.ModExpr.ModuleInfo.MonitorMap.Keys);
            WriteInitializeMonitorMap(context, output, safety.ModExpr.ModuleInfo.MonitorMap);
            WriteTestFunction(context, output, safety.Main);
            context.WriteLine(output, "}");

            WriteNameSpaceEpilogue(context, output);
        }

        private void WriteImplementationDecl(CompilationContext context, StringWriter output, Implementation impl)
        {
            WriteNameSpacePrologue(context, output);

            context.WriteLine(output, $"public class {context.Names.GetNameForDecl(impl)} {{");
            WriteInitializeLinkMap(context, output, impl.ModExpr.ModuleInfo.LinkMap);
            WriteInitializeInterfaceDefMap(context, output, impl.ModExpr.ModuleInfo.InterfaceDef);
            WriteInitializeMonitorObserves(context, output, impl.ModExpr.ModuleInfo.MonitorMap.Keys);
            WriteInitializeMonitorMap(context, output, impl.ModExpr.ModuleInfo.MonitorMap);
            WriteTestFunction(context, output, impl.Main);
            context.WriteLine(output, "}");

            WriteNameSpaceEpilogue(context, output);
        }

        private void WriteInitializeMonitorObserves(CompilationContext context, StringWriter output,
            ICollection<Machine> monitors)
        {
            context.WriteLine(output, "public static void InitializeMonitorObserves() {");
            context.WriteLine(output, "PModule.monitorObserves.Clear();");
            foreach (Machine monitor in monitors)
            {
                context.WriteLine(output,
                    $"PModule.monitorObserves[nameof({context.Names.GetNameForDecl(monitor)})] = new List<string>();");
                foreach (PEvent ev in monitor.Observes.Events)
                {
                    context.WriteLine(output,
                        $"PModule.monitorObserves[nameof({context.Names.GetNameForDecl(monitor)})].Add(nameof({context.Names.GetNameForDecl(ev)}));");
                }
            }

            context.WriteLine(output, "}");
            context.WriteLine(output);
        }

        private void WriteInitializeEnums(CompilationContext context, StringWriter output, IEnumerable<PEnum> enums)
        {
            WriteNameSpacePrologue(context, output);
            //initialize the interfaces
            context.WriteLine(output, "public partial class PHelper {");
            context.WriteLine(output, "public static void InitializeEnums() {");
            context.WriteLine(output, "PrtEnum.Clear();");
            foreach (PEnum enumDecl in enums)
            {
                string enumElemNames =
                    $"new [] {{{string.Join(",", enumDecl.Values.Select(e => $"\"{context.Names.GetNameForDecl(e)}\""))}}}";
                string enumElemValues = $"new [] {{{string.Join(",", enumDecl.Values.Select(e => e.Value))}}}";
                context.WriteLine(output, $"PrtEnum.AddEnumElements({enumElemNames}, {enumElemValues});");
            }

            context.WriteLine(output, "}");
            context.WriteLine(output, "}");
            context.WriteLine(output);

            WriteNameSpaceEpilogue(context, output);
        }

        private void WriteTestFunction(CompilationContext context, StringWriter output, string main)
        {
            context.WriteLine(output);
            context.WriteLine(output, "[Microsoft.Coyote.SystematicTesting.Test]");
            context.WriteLine(output, "public static void Execute(IActorRuntime runtime) {");
            context.WriteLine(output, "runtime.RegisterLog(new PLogFormatter());");
            context.WriteLine(output, "PModule.runtime = runtime;");
            context.WriteLine(output, "PHelper.InitializeInterfaces();");
            context.WriteLine(output, "PHelper.InitializeEnums();");
            context.WriteLine(output, "InitializeLinkMap();");
            context.WriteLine(output, "InitializeInterfaceDefMap();");
            context.WriteLine(output, "InitializeMonitorMap(runtime);");
            context.WriteLine(output, "InitializeMonitorObserves();");
            context.WriteLine(output,
                $"runtime.CreateActor(typeof(_GodMachine), new _GodMachine.Config(typeof({main})));");
            context.WriteLine(output, "}");
        }

        private void WriteInitializeMonitorMap(CompilationContext context, StringWriter output,
            IDictionary<Machine, IEnumerable<Interface>> monitorMap)
        {
            // compute the reverse map
            Dictionary<Interface, List<Machine>> machineMap = new Dictionary<Interface, List<Machine>>();
            foreach (KeyValuePair<Machine, IEnumerable<Interface>> monitorToInterface in monitorMap)
            {
                foreach (Interface iface in monitorToInterface.Value)
                {
                    if (!machineMap.ContainsKey(iface))
                    {
                        machineMap[iface] = new List<Machine>();
                    }

                    machineMap[iface].Add(monitorToInterface.Key);
                }
            }

            context.WriteLine(output, "public static void InitializeMonitorMap(IActorRuntime runtime) {");
            context.WriteLine(output, "PModule.monitorMap.Clear();");
            foreach (KeyValuePair<Interface, List<Machine>> machine in machineMap)
            {
                context.WriteLine(output, $"PModule.monitorMap[nameof({context.Names.GetNameForDecl(machine.Key)})] = new List<Type>();");
                foreach (Machine monitor in machine.Value)
                {
                    context.WriteLine(output,
                        $"PModule.monitorMap[nameof({context.Names.GetNameForDecl(machine.Key)})].Add(typeof({context.Names.GetNameForDecl(monitor)}));");
                }
            }

            foreach (Machine monitor in monitorMap.Keys)
            {
                context.WriteLine(output, $"runtime.RegisterMonitor<{context.Names.GetNameForDecl(monitor)}>();");
            }

            context.WriteLine(output, "}");
            context.WriteLine(output);
        }

        private void WriteInitializeInterfaceDefMap(CompilationContext context, StringWriter output,
            IDictionary<Interface, Machine> interfaceDef)
        {
            context.WriteLine(output, "public static void InitializeInterfaceDefMap() {");
            context.WriteLine(output, "PModule.interfaceDefinitionMap.Clear();");
            foreach (KeyValuePair<Interface, Machine> map in interfaceDef)
            {
                context.WriteLine(output,
                    $"PModule.interfaceDefinitionMap.Add(nameof({context.Names.GetNameForDecl(map.Key)}), typeof({context.Names.GetNameForDecl(map.Value)}));");
            }

            context.WriteLine(output, "}");
            context.WriteLine(output);
        }

        private void WriteInitializeLinkMap(CompilationContext context, StringWriter output,
            IDictionary<Interface, IDictionary<Interface, Interface>> linkMap)
        {
            context.WriteLine(output, "public static void InitializeLinkMap() {");
            context.WriteLine(output, "PModule.linkMap.Clear();");
            foreach (KeyValuePair<Interface, IDictionary<Interface, Interface>> creatorInterface in linkMap)
            {
                context.WriteLine(output,
                    $"PModule.linkMap[nameof({context.Names.GetNameForDecl(creatorInterface.Key)})] = new Dictionary<string, string>();");
                foreach (KeyValuePair<Interface, Interface> clinkMap in creatorInterface.Value)
                {
                    context.WriteLine(output,
                        $"PModule.linkMap[nameof({context.Names.GetNameForDecl(creatorInterface.Key)})].Add(nameof({context.Names.GetNameForDecl(clinkMap.Key)}), nameof({context.Names.GetNameForDecl(clinkMap.Value)}));");
                }
            }

            context.WriteLine(output, "}");
            context.WriteLine(output);
        }

        private void WriteEvent(CompilationContext context, StringWriter output, PEvent pEvent)
        {
            WriteNameSpacePrologue(context, output);

            string declName = context.Names.GetNameForDecl(pEvent);

            // initialize the payload type
            string payloadType = GetCSharpType(pEvent.PayloadType, true);
            context.WriteLine(output, $"internal partial class {declName} : PEvent");
            context.WriteLine(output, "{");
            context.WriteLine(output, $"public {declName}() : base() {{}}");
            context.WriteLine(output, $"public {declName} ({payloadType} payload): base(payload)" + "{ }");
            context.WriteLine(output, $"public override IPrtValue Clone() {{ return new {declName}();}}");
            context.WriteLine(output, "}");

            WriteNameSpaceEpilogue(context, output);

            
        }

        private static string DefaultFunName = "default_function";

        private static string DefaultFunction = $@"
    fn {DefaultFunName}(&mut self, e: {EventTypeName}) {{
        self.common_data.execution_status = MP::default_status();
        return
    }}
";

        private void WriteMachine(CompilationContext context, StringWriter output, Machine machine)
        {
            context.WriteLine(output, "/*");
            WriteNameSpacePrologue(context, output);

            string machine_name = context.Names.GetNameForDecl(machine);
            context.WriteLine(output, $"internal partial class {machine_name} : PMachine");
            context.WriteLine(output, "{");

            foreach (Variable field in machine.Fields)
            {
                context.WriteLine(output,
                    $"private {GetCSharpType(field.Type)} {context.Names.GetNameForDecl(field)} = {GetDefaultValue(field.Type)};");
            }

            //create the constructor event
            string cTorType = GetCSharpType(machine.PayloadType, true);
            context.Write(output, "public class ConstructorEvent : PEvent");
            context.Write(output, "{");
            context.Write(output, $"public ConstructorEvent({cTorType} val) : base(val) {{ }}");
            context.WriteLine(output, "}");
            context.WriteLine(output);

            context.WriteLine(output,
                $"protected override Event GetConstructorEvent(IPrtValue value) {{ return new ConstructorEvent(({cTorType})value); }}");

            // create the constructor to initialize the sends, creates and receives list
            WriteCSharpMachineConstructor(context, output, machine);

            foreach (Function method in machine.Methods)
            {
                WriteFunction(context, output, method);
            }

            foreach (State state in machine.States)
            {
                WriteState(context, output, state);
            }

            context.WriteLine(output, "}");

            WriteNameSpaceEpilogue(context, output);

            context.WriteLine(output, "*/");

            // Rust Code Begins
            // enum for all states of machine
            WriteStates(context, output, machine.States, machine_name);

            // machine declaration
            context.WriteLine(output, $"pub struct {machine_name}");
            context.WriteLine(output, "{");
            context.WriteLine(output, $"common_data: MD::CommonMachineData<{EventTypeName}, {machine_name}State>,");
            foreach (Variable field in machine.Fields)
            {
                context.WriteLine(output, $"{context.Names.GetNameForDecl(field)}: {GetRustType(field.Type)},");
            }
            context.WriteLine(output, "}");

            // implementation block
            context.WriteLine(output, $"impl {machine_name}");
            context.WriteLine(output, "{");

            // constructor
            WriteMachineConstructor(context, output, machine);

            // transition functions
            foreach (Function method in machine.Methods)
            {
                WriteRustFunction(context, output, method, machine_name, machine.Fields);
            }

            context.WriteLine(output, DefaultFunction);

            // ending the impl for state machine
            context.WriteLine(output, "}");
            context.WriteLine(output, "");

            WriteMachineTrait(context, output, machine);
            // Rust Code Ends
        }

        private static string SendEventFunDecl = @"
    fn send_event(&self, receiver: M::Index, event: Self::Event) {
        self.common_data.send_event(receiver, event)
    }";

        private static string CreateMachineFunDecl = @"
    fn create_machine(&self, name: &'static str, args: PV::PValue) -> M::Index {
        let machine = create_new_machine(name, args);
        let create_machine_req = MP::MachineToConfigMsg::CreateMachineRequest(machine);
        self.common_data
            .tx_machine_to_config
            .send(create_machine_req)
            .unwrap();

        if let MP::ConfigToMachineMsg::CreateMachineResponse(new_index) =
            self.common_data.rx_config_to_machine.recv().unwrap()
        {
            new_index
        } else {
            panic!(
                ""Machine {} did not receive create machine response"",
                self.common_data.self_id.print()
            )
        }
    }";

        private static string AddToConfigFunDecl = @"
    fn add_to_config(&mut self) {
        self.common_data.add_to_config()
    }";

        private static string AnswerRequestsFunDecl = @"
    fn answer_requests(&mut self) {
        loop {
            let req = self.common_data.rx_config_to_machine.recv().unwrap();

            match req {
                MP::ConfigToMachineMsg::ExecuteRequest(e_opt) => {
                    self.execute(e_opt);
                    let execution_status = self.common_data.execution_status;
                    self.common_data
                        .tx_machine_to_config
                        .send(MP::MachineToConfigMsg::ExecuteResponse(execution_status))
                        .unwrap();

                    if matches!(execution_status, MP::ExecutionStatus::Terminated) {
                        break;
                    }
                }
                _ => {
                    panic!(
                        ""Machine {} received an invalid request"",
                        self.common_data.self_id.print()
                    )
                }
            }
        }
    }";

        private static string NameFunDecl = @"
    fn name(&self) -> &'static str {
        self.common_data.name()
    }";

        private static string IndexFunDecl = @"
    fn index(&self) -> M::Index {
        self.common_data.index()
    }";

        private static string PrintFunDecl = @"
    fn print(&self) {
        self.common_data.print()
    }";

        private static string UpdateIdFunDecl = @"
    fn update_id(&mut self, instance: i32) {
        self.common_data.update_id(instance)
    }";

        private void WriteMachineTrait(CompilationContext context, StringWriter output, Machine machine)
        {
            string machine_name = context.Names.GetNameForDecl(machine);
            context.WriteLine(output, $"impl PStateMachine for {machine_name} {{");

            context.WriteLine(output, $"type Event = {EventTypeName};");

            context.WriteLine(output, SendEventFunDecl);

            context.WriteLine(output, CreateMachineFunDecl);

            context.WriteLine(output, AddToConfigFunDecl);

            context.WriteLine(output, AnswerRequestsFunDecl);

            context.WriteLine(output, NameFunDecl);

            context.WriteLine(output, IndexFunDecl);

            WriteExecuteFunDecl(context, output, machine);

            context.WriteLine(output, PrintFunDecl);

            context.WriteLine(output, UpdateIdFunDecl);

            context.WriteLine(output, "}");
        }

        private void WriteExecuteFunDecl(CompilationContext context, StringWriter output, Machine machine)
        {
            context.WriteLine(output, "");

            context.WriteLine(output, "fn execute(&mut self, event: Self::Event) {");

            context.WriteLine(output, "match self.common_data.current_state {");

            string machine_name = context.Names.GetNameForDecl(machine);

            foreach (State state in machine.States)
            {
                WriteTransitionFunctionsForState(context, output, state, machine_name);
            }

            context.WriteLine(output, "}");

            context.WriteLine(output, "}");
        }

        private void WriteTransitionFunctionsForState(CompilationContext context, StringWriter output, State state, string machine_name)
        {
            string state_name = context.Names.GetNameForDecl(state);

            context.WriteLine(output, $"{machine_name}State::{state_name} =>");

            context.WriteLine(output, "{");

            context.WriteLine(output, "match event.name");

            context.WriteLine(output, "{");

            if (state.Entry != null)
            {
                string entry_fname = context.Names.GetNameForDecl(state.Entry);
                string event_or_ctor = state.IsStart ? $"{EventTypeName} {{ name: {EventNameTypeName}::DefaultEvent, payload: self.common_data.payload.clone() }}" : "event";
                context.WriteLine(
                        output,
                        $"{EventNameTypeName}::{DefaultEventName} => self.{entry_fname}({event_or_ctor}),");
            }
            else
            {
                context.WriteLine(
                        output,
                        $"{EventNameTypeName}::{DefaultEventName} => self.{DefaultFunName}(event),");
            }

            foreach (KeyValuePair<PEvent, IStateAction> ev_handler in state.AllEventHandlers)
            {
                PEvent ev = ev_handler.Key;
                IStateAction handler = ev_handler.Value;

                string ev_name = context.Names.GetNameForDecl(ev);

                switch (handler)
                {
                    case EventDoAction eventDoAction:
                        var targetDoFunctionName = context.Names.GetNameForDecl(eventDoAction.Target);
                        targetDoFunctionName = eventDoAction.Target.IsAnon ? targetDoFunctionName : $"_{targetDoFunctionName}";
                        context.WriteLine(
                            output,
                            $"{EventNameTypeName}::{ev_name} => self.{targetDoFunctionName}(event),");
                        break;

                    default:
                        throw new NotImplementedException();
                }
            }

            context.WriteLine(output, @"_ => panic!(""unhandled event {:?} in state {:?}"", event.name, self.common_data.current_state),");


            context.WriteLine(output, "}");

            context.WriteLine(output, "}");
        }

        private void WriteStates(CompilationContext context, StringWriter output,
            IEnumerable<State> states, string machine_name)
        {
            context.WriteLine(output, "#[derive(Debug)]");
            context.WriteLine(output, $"enum {machine_name}State");
            context.WriteLine(output, "{");
            foreach (State state in states)
            {
                context.WriteLine(output, $"{context.Names.GetNameForDecl(state)},");
            }
            context.WriteLine(output, "}");
        }

        private static void WriteCSharpMachineConstructor(CompilationContext context, StringWriter output, Machine machine)
        {
            string declName = context.Names.GetNameForDecl(machine);
            context.WriteLine(output, $"public {declName}() {{");
            foreach (PEvent sEvent in machine.Sends.Events)
            {
                context.WriteLine(output, $"this.sends.Add(nameof({context.Names.GetNameForDecl(sEvent)}));");
            }

            foreach (PEvent rEvent in machine.Receives.Events)
            {
                context.WriteLine(output, $"this.receives.Add(nameof({context.Names.GetNameForDecl(rEvent)}));");
            }

            foreach (Interface iCreate in machine.Creates.Interfaces)
            {
                context.WriteLine(output, $"this.creates.Add(nameof({context.Names.GetNameForDecl(iCreate)}));");
            }

            context.WriteLine(output, "}");
            context.WriteLine(output);
        }

        private void WriteMachineConstructor(CompilationContext context, StringWriter output, Machine machine)
        {
            string declName = context.Names.GetNameForDecl(machine);
            context.WriteLine(output, "pub fn new(");
            context.WriteLine(output, $"tx_machine_to_config: mpsc::Sender<MP::MachineToConfigMsg<{EventTypeName}>>,");
            context.WriteLine(output, $"rx_config_to_machine: mpsc::Receiver<MP::ConfigToMachineMsg<{EventTypeName}>>,");
            string arg_type = "PV::PValue";
            context.WriteLine(output, $"arg: {arg_type}) -> Box<Self>");
            context.WriteLine(output, "{");
            context.WriteLine(output, $"let name = \"{declName}\";");
            context.WriteLine(output, $"let self_id = M::Index::create_index(name, 0);");
            context.WriteLine(output, $"let current_state = {declName}State::{context.Names.GetNameForDecl(machine.StartState)};");
            foreach (Variable field in machine.Fields)
            {
                context.WriteLine(output, $"let {context.Names.GetNameForDecl(field)} = {DefaultValueForType(field.Type)};");
            }

            context.WriteLine(output, $"Box::new({declName}");
            context.WriteLine(output, "{");
            context.WriteLine(output, "common_data: MD::CommonMachineData::create(");
            context.WriteLine(output, "  name,");
            context.WriteLine(output, "  self_id,");
            context.WriteLine(output, "  current_state,");
            context.WriteLine(output, "  tx_machine_to_config,");
            context.WriteLine(output, "  rx_config_to_machine,");
            context.WriteLine(output, "  arg,");
            context.WriteLine(output, "),");
            foreach (Variable field in machine.Fields)
            {
                context.WriteLine(output, $"{context.Names.GetNameForDecl(field)},");
            }
            context.WriteLine(output, "})");
            context.WriteLine(output, "}");

        }

        private void WriteState(CompilationContext context, StringWriter output, State state)
        {
            if (state.IsStart && !state.OwningMachine.IsSpec)
            {
                context.WriteLine(output, "[Start]");
                context.WriteLine(output, "[OnEntry(nameof(InitializeParametersFunction))]");
                context.WriteLine(output,
                    $"[OnEventGotoState(typeof(ConstructorEvent), typeof({context.Names.GetNameForDecl(state)}))]");
                context.WriteLine(output, "class __InitState__ : State { }");
                context.WriteLine(output);
            }

            if (state.IsStart && state.OwningMachine.IsSpec)
            {
                context.WriteLine(output, "[Start]");
            }

            if (state.OwningMachine.IsSpec)
            {
                if (state.Temperature == StateTemperature.Cold)
                {
                    context.WriteLine(output, "[Cold]");
                }
                else if (state.Temperature == StateTemperature.Hot)
                {
                    context.WriteLine(output, "[Hot]");
                }
            }

            if (state.Entry != null)
            {
                context.WriteLine(output, $"[OnEntry(nameof({context.Names.GetNameForDecl(state.Entry)}))]");
            }

            List<string> deferredEvents = new List<string>();
            List<string> ignoredEvents = new List<string>();
            foreach (KeyValuePair<PEvent, IStateAction> eventHandler in state.AllEventHandlers)
            {
                PEvent pEvent = eventHandler.Key;
                IStateAction stateAction = eventHandler.Value;
                switch (stateAction)
                {
                    case EventDefer _:
                        deferredEvents.Add($"typeof({context.Names.GetNameForDecl(pEvent)})");
                        break;

                    case EventDoAction eventDoAction:
                        var targetDoFunctionName = context.Names.GetNameForDecl(eventDoAction.Target);
                        targetDoFunctionName = eventDoAction.Target.IsAnon ? targetDoFunctionName : $"_{targetDoFunctionName}";
                        context.WriteLine(
                            output,
                            $"[OnEventDoAction(typeof({context.Names.GetNameForDecl(pEvent)}), nameof({targetDoFunctionName}))]");
                        break;

                    case EventGotoState eventGotoState when eventGotoState.TransitionFunction == null:
                        context.WriteLine(
                            output,
                            $"[OnEventGotoState(typeof({context.Names.GetNameForDecl(pEvent)}), typeof({context.Names.GetNameForDecl(eventGotoState.Target)}))]");
                        break;

                    case EventGotoState eventGotoState when eventGotoState.TransitionFunction != null:
                        var targetGotoFunctionName = context.Names.GetNameForDecl(eventGotoState.TransitionFunction);
                        targetGotoFunctionName = eventGotoState.TransitionFunction.IsAnon ? targetGotoFunctionName : $"_{targetGotoFunctionName}";
                        context.WriteLine(
                            output,
                            $"[OnEventGotoState(typeof({context.Names.GetNameForDecl(pEvent)}), typeof({context.Names.GetNameForDecl(eventGotoState.Target)}), nameof({targetGotoFunctionName}))]");
                        break;

                    case EventIgnore _:
                        ignoredEvents.Add($"typeof({context.Names.GetNameForDecl(pEvent)})");
                        break;

                    case EventPushState eventPushState:
                        context.WriteLine(
                            output,
                            $"[OnEventPushState(typeof({context.Names.GetNameForDecl(pEvent)}), typeof({context.Names.GetNameForDecl(eventPushState.Target)}))]");
                        break;
                }
            }

            if (deferredEvents.Count > 0)
            {
                context.WriteLine(output, $"[DeferEvents({string.Join(", ", deferredEvents.AsEnumerable())})]");
            }

            if (ignoredEvents.Count > 0)
            {
                context.WriteLine(output, $"[IgnoreEvents({string.Join(", ", ignoredEvents.AsEnumerable())})]");
            }

            if (state.Exit != null)
            {
                context.WriteLine(output, $"[OnExit(nameof({context.Names.GetNameForDecl(state.Exit)}))]");
            }

            context.WriteLine(output, $"class {context.Names.GetNameForDecl(state)} : State");
            context.WriteLine(output, "{");
            context.WriteLine(output, "}");
        }

        private void WriteNamedFunctionWrapper(CompilationContext context, StringWriter output, Function function)
        {
            if (function.Role == FunctionRole.Method || function.Role == FunctionRole.Foreign)
                return;

            bool isAsync = function.CanReceive == true;
            FunctionSignature signature = function.Signature;

            string functionName = context.Names.GetNameForDecl(function);
            string functionParameters = "Event currentMachine_dequeuedEvent";
            string awaitMethod = isAsync ? "await " : "";
            string asyncMethod = isAsync ? "async" : "";
            string returnType = "void";

            if (isAsync)
            {
                returnType = "Task";
            }

            context.WriteLine(output,
                $"public {asyncMethod} {returnType} {$"_{functionName}"}({functionParameters})");

            context.WriteLine(output, "{");

            //add the declaration of currentMachine
            if (function.Owner != null)
            {
                context.WriteLine(output, $"{context.Names.GetNameForDecl(function.Owner)} currentMachine = this;");
            }

            var parameter = function.Signature.Parameters.Any() ? $"({GetCSharpType(function.Signature.ParameterTypes.First())})((PEvent)currentMachine_dequeuedEvent).Payload" : "";
            context.WriteLine(output, $"{awaitMethod}{functionName}({parameter});");
            context.WriteLine(output, "}");
        }

        private void WriteFunction(CompilationContext context, StringWriter output, Function function)
        {
            if (function.IsForeign)
            {
                return;
            }

            bool isStatic = function.Owner == null;

            if (!function.IsAnon && !isStatic)
            {
                WriteNamedFunctionWrapper(context, output, function);
            }

            bool isAsync = function.CanReceive == true;
            FunctionSignature signature = function.Signature;

            string staticKeyword = isStatic ? "static " : "";
            string asyncKeyword = isAsync ? "async " : "";
            string returnType = GetCSharpType(signature.ReturnType);

            if (isAsync)
            {
                returnType = returnType == "void" ? "Task" : $"Task<{returnType}>";
            }

            string functionName = context.Names.GetNameForDecl(function);
            string functionParameters = "";
            if (function.IsAnon)
            {
                functionParameters = "Event currentMachine_dequeuedEvent";
            }
            else
            {
                functionParameters = string.Join(
                    ", ",
                    signature.Parameters.Select(param =>
                        $"{GetCSharpType(param.Type)} {context.Names.GetNameForDecl(param)}"));
            }

            if (isStatic)
            {
                string seperator = functionParameters == "" ? "" : ", ";
                functionParameters += string.Concat(seperator, "PMachine currentMachine");
            }

            context.WriteLine(output,
                $"public {staticKeyword}{asyncKeyword}{returnType} {functionName}({functionParameters})");
            WriteFunctionBody(context, output, function);
        }

        private void WriteFunctionBody(CompilationContext context, StringWriter output, Function function)
        {
            context.WriteLine(output, "{");

            //add the declaration of currentMachine
            if (function.Owner != null)
            {
                context.WriteLine(output, $"{context.Names.GetNameForDecl(function.Owner)} currentMachine = this;");
            }

            if (function.IsAnon)
            {
                if (function.Signature.Parameters.Any())
                {
                    Variable param = function.Signature.Parameters.First();
                    context.WriteLine(output,
                        $"{GetCSharpType(param.Type)} {context.Names.GetNameForDecl(param)} = ({GetCSharpType(param.Type)})(gotoPayload ?? ((PEvent)currentMachine_dequeuedEvent).Payload);");
                    context.WriteLine(output, "this.gotoPayload = null;");
                }
            }

            foreach (Variable local in function.LocalVariables)
            {
                PLanguageType type = local.Type;
                context.WriteLine(output,
                    $"{GetCSharpType(type, true)} {context.Names.GetNameForDecl(local)} = {GetDefaultValue(type)};");
            }

            foreach (IPStmt bodyStatement in function.Body.Statements)
            {
                WriteStmt(context: context, output: output, function: function, stmt: bodyStatement);
            }

            context.WriteLine(output, "}");
        }

        private void WriteStmt(CompilationContext context, StringWriter output, Function function, IPStmt stmt)
        {
            switch (stmt)
            {
                case AnnounceStmt announceStmt:
                    context.Write(output, "currentMachine.Announce((Event)");
                    WriteExpr(context, output, announceStmt.PEvent);
                    if (announceStmt.Payload != null)
                    {
                        context.Write(output, ", ");
                        WriteExpr(context, output, announceStmt.Payload);
                    }

                    context.WriteLine(output, ");");
                    break;

                case AssertStmt assertStmt:
                    context.Write(output, "currentMachine.TryAssert(");
                    WriteExpr(context, output, assertStmt.Assertion);
                    context.Write(output, ",");
                    context.Write(output, $"\"Assertion Failed: \" + ");
                    WriteExpr(context, output, assertStmt.Message);
                    context.WriteLine(output, ");");
                    //last statement
                    if (FunctionValidator.SurelyReturns(assertStmt))
                    {
                        context.WriteLine(output, "throw new PUnreachableCodeException();");
                    }

                    break;

                case AssignStmt assignStmt:
                    bool needCtorAdapter = !assignStmt.Value.Type.IsSameTypeAs(assignStmt.Location.Type)
                                          && !PrimitiveType.Null.IsSameTypeAs(assignStmt.Value.Type)
                                          && !PrimitiveType.Any.IsSameTypeAs(assignStmt.Location.Type);
                    WriteLValue(context, output, assignStmt.Location);
                    context.Write(output, $" = ({GetCSharpType(assignStmt.Location.Type)})(");
                    if (needCtorAdapter)
                    {
                        context.Write(output, $"new {GetCSharpType(assignStmt.Location.Type)}(");
                    }

                    WriteExpr(context, output, assignStmt.Value);
                    if (needCtorAdapter)
                    {
                        if (assignStmt.Location.Type.Canonicalize() is SequenceType seqType)
                        {
                            context.Write(output, $".Cast<{GetCSharpType(seqType.ElementType)}>()");
                        }

                        context.Write(output, ")");
                    }

                    context.WriteLine(output, ");");
                    break;

                case CompoundStmt compoundStmt:
                    context.WriteLine(output, "{");
                    foreach (IPStmt subStmt in compoundStmt.Statements)
                    {
                        WriteStmt(context, output, function, subStmt);
                    }

                    context.WriteLine(output, "}");
                    break;

                case CtorStmt ctorStmt:
                    context.Write(output,
                        $"currentMachine.CreateInterface<{context.Names.GetNameForDecl(ctorStmt.Interface)}>(");
                    context.Write(output, "currentMachine");
                    if (ctorStmt.Arguments.Any())
                    {
                        context.Write(output, ", ");
                        if (ctorStmt.Arguments.Count > 1)
                        {
                            //create tuple from rvaluelist
                            context.Write(output, "new PrtTuple(");
                            string septor = "";
                            foreach (IPExpr ctorExprArgument in ctorStmt.Arguments)
                            {
                                context.Write(output, septor);
                                WriteExpr(context, output, ctorExprArgument);
                                septor = ",";
                            }

                            context.Write(output, ")");
                        }
                        else
                        {
                            WriteExpr(context, output, ctorStmt.Arguments.First());
                        }
                    }

                    context.WriteLine(output, ");");
                    break;

                case FunCallStmt funCallStmt:
                    bool isStatic = funCallStmt.Function.Owner == null;
                    string awaitMethod = funCallStmt.Function.CanReceive == true ? "await " : "";
                    string globalFunctionClass = isStatic ? $"{context.GlobalFunctionClassName}." : "";
                    context.Write(output,
                        $"{awaitMethod}{globalFunctionClass}{context.Names.GetNameForDecl(funCallStmt.Function)}(");
                    string separator = "";

                    foreach (IPExpr param in funCallStmt.ArgsList)
                    {
                        context.Write(output, separator);
                        WriteExpr(context, output, param);
                        separator = ", ";
                    }

                    if (isStatic)
                    {
                        context.Write(output, separator + "currentMachine");
                    }

                    context.WriteLine(output, ");");
                    break;

                case GotoStmt gotoStmt:
                    //last statement
                    context.Write(output, $"currentMachine.TryGotoState<{context.Names.GetNameForDecl(gotoStmt.State)}>(");
                    if (gotoStmt.Payload != null)
                    {
                        WriteExpr(context, output, gotoStmt.Payload);
                    }

                    context.WriteLine(output, ");");
                    context.WriteLine(output, "return;");
                    break;

                case IfStmt ifStmt:
                    context.Write(output, "if (");
                    WriteExpr(context, output, ifStmt.Condition);
                    context.WriteLine(output, ")");
                    WriteStmt(context, output, function, ifStmt.ThenBranch);
                    if (ifStmt.ElseBranch != null && ifStmt.ElseBranch.Statements.Any())
                    {
                        context.WriteLine(output, "else");
                        WriteStmt(context, output, function, ifStmt.ElseBranch);
                    }

                    break;

                case AddStmt addStmt:
                    context.Write(output, "((PrtSet)");
                    WriteExpr(context, output, addStmt.Variable);
                    context.Write(output, ").Add(");
                    WriteExpr(context, output, addStmt.Value);
                    context.WriteLine(output, ");");
                    break;

                case InsertStmt insertStmt:
                    bool isMap = PLanguageType.TypeIsOfKind(insertStmt.Variable.Type, TypeKind.Map);
                    string castOp = isMap ? "(PrtMap)" : "(PrtSeq)";
                    context.Write(output, $"({castOp}");
                    WriteExpr(context, output, insertStmt.Variable);
                    if (isMap)
                    {
                        context.Write(output, ").Add(");
                    }
                    else
                    {
                        context.Write(output, ").Insert(");
                    }

                    WriteExpr(context, output, insertStmt.Index);
                    context.Write(output, ", ");
                    WriteExpr(context, output, insertStmt.Value);
                    context.WriteLine(output, ");");
                    break;

                case MoveAssignStmt moveAssignStmt:
                    string upCast = "";
                    if (!moveAssignStmt.FromVariable.Type.IsSameTypeAs(moveAssignStmt.ToLocation.Type))
                    {
                        upCast = $"({GetCSharpType(moveAssignStmt.ToLocation.Type)})";
                        if (PLanguageType.TypeIsOfKind(moveAssignStmt.FromVariable.Type, TypeKind.Enum))
                        {
                            upCast = $"{upCast}(long)";
                        }
                    }

                    WriteLValue(context, output, moveAssignStmt.ToLocation);
                    context.WriteLine(output,
                        $" = {upCast}{context.Names.GetNameForDecl(moveAssignStmt.FromVariable)};");
                    break;

                case NoStmt _:
                    break;

                case PopStmt _:
                    //last statement
                    context.WriteLine(output, "currentMachine.TryPopState();");
                    context.WriteLine(output, "return;");
                    break;

                case PrintStmt printStmt:
                    context.Write(output, $"PModule.runtime.Logger.WriteLine(\"<PrintLog> \" + ");
                    WriteExpr(context, output, printStmt.Message);
                    context.WriteLine(output, ");");
                    break;

                case RaiseStmt raiseStmt:
                    //last statement
                    context.Write(output, "currentMachine.TryRaiseEvent((Event)");
                    WriteExpr(context, output, raiseStmt.PEvent);
                    if (raiseStmt.Payload.Any())
                    {
                        context.Write(output, ", ");
                        WriteExpr(context, output, raiseStmt.Payload.First());
                    }

                    context.WriteLine(output, ");");
                    context.WriteLine(output, "return;");
                    break;

                case ReceiveStmt receiveStmt:
                    string eventName = context.Names.GetTemporaryName("recvEvent");
                    string[] eventTypeNames = receiveStmt.Cases.Keys.Select(evt => context.Names.GetNameForDecl(evt))
                        .ToArray();
                    string recvArgs = string.Join(", ", eventTypeNames.Select(name => $"typeof({name})"));
                    context.WriteLine(output, $"var {eventName} = await currentMachine.TryReceiveEvent({recvArgs});");
                    context.WriteLine(output, $"switch ({eventName}) {{");
                    foreach (KeyValuePair<PEvent, Function> recvCase in receiveStmt.Cases)
                    {
                        string caseName = context.Names.GetTemporaryName("evt");
                        context.WriteLine(output, $"case {context.Names.GetNameForDecl(recvCase.Key)} {caseName}: {{");
                        if (recvCase.Value.Signature.Parameters.FirstOrDefault() is Variable caseArg)
                        {
                            context.WriteLine(output,
                                $"{GetCSharpType(caseArg.Type)} {context.Names.GetNameForDecl(caseArg)} = ({GetCSharpType(caseArg.Type)})({caseName}.Payload);");
                        }

                        foreach (Variable local in recvCase.Value.LocalVariables)
                        {
                            PLanguageType type = local.Type;
                            context.WriteLine(output,
                                $"{GetCSharpType(type, true)} {context.Names.GetNameForDecl(local)} = {DefaultValueForType(type)};");
                        }

                        foreach (IPStmt caseStmt in recvCase.Value.Body.Statements)
                        {
                            WriteStmt(context, output, function, caseStmt);
                        }

                        context.WriteLine(output, "} break;");
                    }

                    context.WriteLine(output, "}");
                    break;

                case RemoveStmt removeStmt:
                    {
                        string castOperation = PLanguageType.TypeIsOfKind(removeStmt.Variable.Type, TypeKind.Map)
                        ? "(PrtMap)"
                        : PLanguageType.TypeIsOfKind(removeStmt.Variable.Type, TypeKind.Sequence)
                        ? "(PrtSeq)"
                        : "(PrtSet)";
                        context.Write(output, $"({castOperation}");
                        switch (removeStmt.Variable.Type.Canonicalize())
                        {
                            case MapType _:
                                WriteExpr(context, output, removeStmt.Variable);
                                context.Write(output, ").Remove(");
                                WriteExpr(context, output, removeStmt.Value);
                                context.WriteLine(output, ");");
                                break;

                            case SequenceType _:
                                WriteExpr(context, output, removeStmt.Variable);
                                context.Write(output, ").RemoveAt(");
                                WriteExpr(context, output, removeStmt.Value);
                                context.WriteLine(output, ");");
                                break;

                            case SetType _:
                                WriteExpr(context, output, removeStmt.Variable);
                                context.Write(output, ").Remove(");
                                WriteExpr(context, output, removeStmt.Value);
                                context.WriteLine(output, ");");
                                break;

                            default:
                                throw new ArgumentOutOfRangeException(
                                    $"Remove cannot be applied to type {removeStmt.Variable.Type.OriginalRepresentation}");
                        }
                        break;
                    }

                case ReturnStmt returnStmt:
                    context.Write(output, "return ");
                    if (returnStmt.ReturnValue != null)
                    {
                        WriteExpr(context, output, returnStmt.ReturnValue);
                    }
                    context.WriteLine(output, ";");
                    break;

                case BreakStmt breakStmt:
                    context.WriteLine(output, "break;");
                    break;

                case ContinueStmt continueStmt:
                    context.WriteLine(output, "continue;");
                    break;

                case SendStmt sendStmt:
                    context.Write(output, "currentMachine.TrySendEvent(");
                    WriteExpr(context, output, sendStmt.MachineExpr);
                    context.Write(output, ", (Event)");
                    WriteExpr(context, output, sendStmt.Evt);

                    if (sendStmt.Arguments.Any())
                    {
                        context.Write(output, ", ");
                        if (sendStmt.Arguments.Count > 1)
                        {
                            //create tuple from rvaluelist
                            string argTypes = string.Join(",",
                                sendStmt.Arguments.Select(a => GetCSharpType(a.Type)));
                            string tupleType = $"PrtTuple";
                            context.Write(output, $"new {tupleType}(");
                            string septor = "";
                            foreach (IPExpr ctorExprArgument in sendStmt.Arguments)
                            {
                                context.Write(output, septor);
                                WriteExpr(context, output, ctorExprArgument);
                                septor = ",";
                            }

                            context.Write(output, ")");
                        }
                        else
                        {
                            WriteExpr(context, output, sendStmt.Arguments.First());
                        }
                    }

                    context.WriteLine(output, ");");
                    break;

                case SwapAssignStmt swapStmt:
                    throw new NotImplementedException("Swap Assignment Not Implemented");

                case WhileStmt whileStmt:
                    context.Write(output, "while (");
                    WriteExpr(context, output, whileStmt.Condition);
                    context.WriteLine(output, ")");
                    WriteStmt(context, output, function, whileStmt.Body);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(stmt));
            }
        }

        private void WriteLValue(CompilationContext context, StringWriter output, IPExpr lvalue)
        {
#pragma warning disable CCN0002 // Non exhaustive patterns in switch block
            switch (lvalue)
            {
                case MapAccessExpr mapAccessExpr:
                    context.Write(output, "((PrtMap)");
                    WriteLValue(context, output, mapAccessExpr.MapExpr);
                    context.Write(output, ")[");
                    WriteExpr(context, output, mapAccessExpr.IndexExpr);
                    context.Write(output, "]");
                    break;
                
                case SetAccessExpr setAccessExpr:
                    context.Write(output, "((PrtSet)");
                    WriteLValue(context, output, setAccessExpr.SetExpr);
                    context.Write(output, ")[");
                    WriteExpr(context, output, setAccessExpr.IndexExpr);
                    context.Write(output, "]");
                    break;
                
                case NamedTupleAccessExpr namedTupleAccessExpr:
                    context.Write(output, "((PrtNamedTuple)");
                    WriteExpr(context, output, namedTupleAccessExpr.SubExpr);
                    context.Write(output, $")[\"{namedTupleAccessExpr.FieldName}\"]");
                    break;

                case SeqAccessExpr seqAccessExpr:
                    context.Write(output, "((PrtSeq)");
                    WriteLValue(context, output, seqAccessExpr.SeqExpr);
                    context.Write(output, ")[");
                    WriteExpr(context, output, seqAccessExpr.IndexExpr);
                    context.Write(output, "]");
                    break;

                case TupleAccessExpr tupleAccessExpr:
                    context.Write(output, "((PrtTuple)");
                    WriteExpr(context, output, tupleAccessExpr.SubExpr);
                    context.Write(output, $")[{tupleAccessExpr.FieldNo}]");
                    break;

                case VariableAccessExpr variableAccessExpr:
                    context.Write(output, context.Names.GetNameForDecl(variableAccessExpr.Variable));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(lvalue));
            }
#pragma warning restore CCN0002 // Non exhaustive patterns in switch block
        }

        private void WriteExpr(CompilationContext context, StringWriter output, IPExpr pExpr)
        {
#pragma warning disable CCN0002 // Non exhaustive patterns in switch block
            switch (pExpr)
            {
                case CloneExpr cloneExpr:
                    WriteClone(context, output, cloneExpr.Term);
                    break;

                case BinOpExpr binOpExpr:
                    //handle eq and noteq differently
                    if (binOpExpr.Operation == BinOpType.Eq || binOpExpr.Operation == BinOpType.Neq)
                    {
                        string negate = binOpExpr.Operation == BinOpType.Neq ? "!" : "";
                        context.Write(output, $"({negate}PrtValues.SafeEquals(");
                        if (PLanguageType.TypeIsOfKind(binOpExpr.Lhs.Type, TypeKind.Enum))
                        {
                            context.Write(output, "PrtValues.Box((long) ");
                            WriteExpr(context, output, binOpExpr.Lhs);
                            context.Write(output, "),");
                        }
                        else
                        {
                            WriteExpr(context, output, binOpExpr.Lhs);
                            context.Write(output, ",");
                        }

                        if (PLanguageType.TypeIsOfKind(binOpExpr.Rhs.Type, TypeKind.Enum))
                        {
                            context.Write(output, "PrtValues.Box((long) ");
                            WriteExpr(context, output, binOpExpr.Rhs);
                            context.Write(output, ")");
                        }
                        else
                        {
                            WriteExpr(context, output, binOpExpr.Rhs);
                        }

                        context.Write(output, "))");
                    }
                    else
                    {
                        context.Write(output, "(");
                        if (PLanguageType.TypeIsOfKind(binOpExpr.Lhs.Type, TypeKind.Enum))
                        {
                            context.Write(output, "(long)");
                        }

                        WriteExpr(context, output, binOpExpr.Lhs);
                        context.Write(output, $") {BinOpToStr(binOpExpr.Operation)} (");
                        if (PLanguageType.TypeIsOfKind(binOpExpr.Rhs.Type, TypeKind.Enum))
                        {
                            context.Write(output, "(long)");
                        }

                        WriteExpr(context, output, binOpExpr.Rhs);
                        context.Write(output, ")");
                    }

                    break;

                case BoolLiteralExpr boolLiteralExpr:
                    context.Write(output, $"((PrtBool){(boolLiteralExpr.Value ? "true" : "false")})");
                    break;

                case CastExpr castExpr:
                    context.Write(output, $"(({GetCSharpType(castExpr.Type)})");
                    WriteExpr(context, output, castExpr.SubExpr);
                    context.Write(output, ")");
                    break;

                case CoerceExpr coerceExpr:
                    switch (coerceExpr.Type.Canonicalize())
                    {
                        case PrimitiveType oldType when oldType.IsSameTypeAs(PrimitiveType.Float):
                        case PrimitiveType oldType1 when oldType1.IsSameTypeAs(PrimitiveType.Int):
                            context.Write(output, "(");
                            WriteExpr(context, output, coerceExpr.SubExpr);
                            context.Write(output, ")");
                            break;

                        case PermissionType _:
                            context.Write(output, "(PInterfaces.IsCoercionAllowed(");
                            WriteExpr(context, output, coerceExpr.SubExpr);
                            context.Write(output, ", ");
                            context.Write(output, $"\"I_{coerceExpr.NewType.CanonicalRepresentation}\") ?");
                            context.Write(output, "new PMachineValue(");
                            context.Write(output, "(");
                            WriteExpr(context, output, coerceExpr.SubExpr);
                            context.Write(output, ").Id, ");
                            context.Write(output,
                                $"PInterfaces.GetPermissions(\"I_{coerceExpr.NewType.CanonicalRepresentation}\")) : null)");
                            break;

                        default:
                            throw new ArgumentOutOfRangeException(
                                @"unexpected coercion operation to:" + coerceExpr.Type.CanonicalRepresentation);
                    }

                    break;

                case ChooseExpr chooseExpr:
                    if (chooseExpr.SubExpr == null)
                    {
                        context.Write(output, "((PrtBool)currentMachine.TryRandomBool())");
                    }
                    else
                    {
                        context.Write(output, $"(({GetCSharpType(chooseExpr.Type)})currentMachine.TryRandom(");
                        WriteExpr(context, output, chooseExpr.SubExpr);
                        context.Write(output, $"))");
                    }
                    break;

                case ContainsExpr containsExpr:
                    var isMap = PLanguageType.TypeIsOfKind(containsExpr.Collection.Type, TypeKind.Map);
                    var isSeq = PLanguageType.TypeIsOfKind(containsExpr.Collection.Type, TypeKind.Sequence);
                    var castOp = isMap ? "(PrtMap)"
                        : isSeq ? "(PrtSeq)"
                        : "(PrtSet)";
                    context.Write(output, "((PrtBool)(");
                    context.Write(output, $"({castOp}");
                    WriteExpr(context, output, containsExpr.Collection);
                    if (isMap)
                    {
                        context.Write(output, ").ContainsKey(");
                    }
                    else
                    {
                        context.Write(output, ").Contains(");
                    }

                    WriteExpr(context, output, containsExpr.Item);
                    context.Write(output, ")))");
                    break;

                case CtorExpr ctorExpr:
                    context.Write(output,
                        $"currentMachine.CreateInterface<{context.Names.GetNameForDecl(ctorExpr.Interface)}>( ");
                    context.Write(output, "currentMachine");
                    if (ctorExpr.Arguments.Any())
                    {
                        context.Write(output, ", ");
                        if (ctorExpr.Arguments.Count > 1)
                        {
                            //create tuple from rvaluelist
                            context.Write(output, "new PrtTuple(");
                            string septor = "";
                            foreach (IPExpr ctorExprArgument in ctorExpr.Arguments)
                            {
                                context.Write(output, septor);
                                WriteExpr(context, output, ctorExprArgument);
                                septor = ",";
                            }

                            context.Write(output, ")");
                        }
                        else
                        {
                            WriteExpr(context, output, ctorExpr.Arguments.First());
                        }
                    }

                    context.Write(output, ")");
                    break;

                case DefaultExpr defaultExpr:
                    context.Write(output, DefaultValueForType(defaultExpr.Type));
                    break;

                case EnumElemRefExpr enumElemRefExpr:
                    EnumElem enumElem = enumElemRefExpr.Value;
                    context.Write(output, $"(PrtEnum.Get(\"{context.Names.GetNameForDecl(enumElem)}\"))");
                    break;

                case EventRefExpr eventRefExpr:
                    string eventName = context.Names.GetNameForDecl(eventRefExpr.Value);
                    switch (eventName)
                    {
                        case "Halt":
                            context.Write(output, "new PHalt()");
                            break;

                        case "DefaultEvent":
                            context.Write(output, "DefaultEvent.Instance");
                            break;

                        default:
                            string payloadExpr = GetDefaultValue(eventRefExpr.Value.PayloadType);
                            context.Write(output, $"new {eventName}({payloadExpr})");
                            break;
                    }

                    break;

                case FairNondetExpr _:
                    context.Write(output, "((PrtBool)currentMachine.TryRandomBool())");
                    break;

                case FloatLiteralExpr floatLiteralExpr:
                    context.Write(output, $"((PrtFloat){floatLiteralExpr.Value})");
                    break;

                case FunCallExpr funCallExpr:
                    bool isStatic = funCallExpr.Function.Owner == null;
                    string awaitMethod = funCallExpr.Function.CanReceive == true ? "await " : "";
                    string globalFunctionClass = isStatic ? $"{context.GlobalFunctionClassName}." : "";
                    context.Write(output,
                        $"{awaitMethod}{globalFunctionClass}{context.Names.GetNameForDecl(funCallExpr.Function)}(");
                    string separator = "";

                    foreach (IPExpr param in funCallExpr.Arguments)
                    {
                        context.Write(output, separator);
                        WriteExpr(context, output, param);
                        separator = ", ";
                    }

                    if (isStatic)
                    {
                        context.Write(output, separator + "currentMachine");
                    }

                    context.Write(output, ")");
                    break;

                case IntLiteralExpr intLiteralExpr:
                    context.Write(output, $"((PrtInt){intLiteralExpr.Value})");
                    break;

                case KeysExpr keysExpr:
                    context.Write(output, "(");
                    WriteExpr(context, output, keysExpr.Expr);
                    context.Write(output, ").CloneKeys()");
                    break;

                case LinearAccessRefExpr linearAccessRefExpr:
                    string swapKeyword = linearAccessRefExpr.LinearType.Equals(LinearType.Swap) ? "ref " : "";
                    context.Write(output, $"{swapKeyword}{context.Names.GetNameForDecl(linearAccessRefExpr.Variable)}");
                    break;

                case NamedTupleExpr namedTupleExpr:
                    string fieldNamesArray = string.Join(",",
                        ((NamedTupleType)namedTupleExpr.Type).Names.Select(n => $"\"{n}\""));
                    fieldNamesArray = $"new string[]{{{fieldNamesArray}}}";
                    context.Write(output, $"(new {GetCSharpType(namedTupleExpr.Type)}({fieldNamesArray}, ");
                    for (int i = 0; i < namedTupleExpr.TupleFields.Count; i++)
                    {
                        if (i > 0)
                        {
                            context.Write(output, ", ");
                        }

                        WriteExpr(context, output, namedTupleExpr.TupleFields[i]);
                    }

                    context.Write(output, "))");
                    break;

                case NondetExpr _:
                    context.Write(output, "((PrtBool)currentMachine.TryRandomBool())");
                    break;

                case NullLiteralExpr _:
                    context.Write(output, "null");
                    break;

                case SizeofExpr sizeofExpr:
                    context.Write(output, "((PrtInt)(");
                    WriteExpr(context, output, sizeofExpr.Expr);
                    context.Write(output, ").Count)");
                    break;

                case StringExpr stringExpr:
                    context.Write(output, $"((PrtString) String.Format(");
                    context.Write(output, $"\"{stringExpr.BaseString}\"");
                    foreach (var arg in stringExpr.Args)
                    {
                        context.Write(output, ",");
                        WriteExpr(context, output, arg);
                    }
                    context.Write(output, "))");
                    break;

                case ThisRefExpr _:
                    context.Write(output, "currentMachine.self");
                    break;

                case UnaryOpExpr unaryOpExpr:
                    context.Write(output, $"{UnOpToStr(unaryOpExpr.Operation)}(");
                    WriteExpr(context, output, unaryOpExpr.SubExpr);
                    context.Write(output, ")");
                    break;

                case UnnamedTupleExpr unnamedTupleExpr:
                    context.Write(output, $"new {GetCSharpType(unnamedTupleExpr.Type)}(");
                    string sep = "";
                    foreach (IPExpr field in unnamedTupleExpr.TupleFields)
                    {
                        context.Write(output, sep);
                        WriteExpr(context, output, field);
                        sep = ", ";
                    }

                    context.Write(output, ")");
                    break;

                case ValuesExpr valuesExpr:
                    context.Write(output, "(");
                    WriteExpr(context, output, valuesExpr.Expr);
                    context.Write(output, ").CloneValues()");
                    break;

                case MapAccessExpr _:
                case SetAccessExpr _:
                case NamedTupleAccessExpr _:
                case SeqAccessExpr _:
                case TupleAccessExpr _:
                case VariableAccessExpr _:
                    WriteLValue(context, output, pExpr);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(pExpr), $"type was {pExpr?.GetType().FullName}");
            }
#pragma warning restore CCN0002 // Non exhaustive patterns in switch block
        }

        private void WriteRustFunction(CompilationContext context, StringWriter output, Function function, string machine_name, IEnumerable<Variable> machine_fields)
        {
            bool isStatic = function.Owner == null;

            bool isAsync = function.CanReceive == true;

            FunctionSignature signature = function.Signature;

            string staticKeyword = isStatic ? "static " : "";
            string asyncKeyword = isAsync ? "async " : "";
            string returnType = GetRustType(signature.ReturnType);

            if (isAsync)
            {
                returnType = returnType == "void" ? "Task" : $"Task<{returnType}>";
            }

            string functionName = context.Names.GetNameForDecl(function);
            string functionParameters = "";
            if (function.IsAnon)
            {
                functionParameters = $"&mut self, e: {EventTypeName}";
            }
            else
            {
                functionParameters = string.Join(
                    ", ",
                    signature.Parameters.Select(param =>
                        $"{GetRustType(param.Type)} {context.Names.GetNameForDecl(param)}"));
            }

            if (isStatic)
            {
                string seperator = functionParameters == "" ? "" : ", ";
                functionParameters += string.Concat(seperator, "PMachine currentMachine");
            }

            context.WriteLine(output,
                $"fn {staticKeyword}{asyncKeyword}{functionName}({functionParameters})");
            WriteRustFunctionBody(context, output, function, machine_name, machine_fields);
            context.WriteLine(output, "");
        }

        private string ExtractionExpr(PLanguageType type)
        {
            string ret = "";

            string param_type = GetRustType(type);

            switch (param_type)
            {
                case "i32":
                    ret = ".extract_int()";
                    break;
                case "M::Index":
                    ret = ".extract_machine()";
                    break;
                case "HashMap<&'static str, PV::PValue>":
                    ret = ".extract_namedtuple()";
                    break;
                case "HashMap<i32, PV::PValue>":
                    ret = ".extract_sequence()";
                    break;
                default:
                    break;

            }

            return ret;
        }

        private void WriteRustFunctionBody(CompilationContext context, StringWriter output, Function function, string machine_name, IEnumerable<Variable> machine_fields)
        {
            context.WriteLine(output, "{");
            context.WriteLine(output, "self.common_data.execution_status = MP::default_status();");

            if (function.IsAnon)
            {
                if (function.Signature.Parameters.Any())
                {
                    Variable param = function.Signature.Parameters.First();
                    context.Write(output, $"let {context.Names.GetNameForDecl(param)} = e.payload");

                    context.Write(output, $"{ExtractionExpr(function.Signature.ParameterTypes.First())}");

                    
                    context.WriteLine(output, ";");
                }
            }

            foreach (Variable local in function.LocalVariables)
            {
                PLanguageType type = local.Type;
                context.WriteLine(output,
                    $"let mut {context.Names.GetNameForDecl(local)} = {DefaultValueForType(type)};");
            }

            foreach (IPStmt bodyStatement in function.Body.Statements)
            {
                WriteRustStmt(context, output, function, bodyStatement, machine_name, machine_fields);
            }

            context.WriteLine(output, "}");
        }

        private static string InterfaceToName(string interface_name)
        {
            return interface_name.Remove(0, 2);
        }

        private void WritePValueExpr(CompilationContext context, StringWriter output, IPExpr expr, IEnumerable<Variable> machine_fields)
        {
            string arg_type = GetRustType(expr.Type);

            switch (arg_type)
            {
                case "i32":
                    context.Write(output, $"PV::PValue::Int(");
                    break;
                case "M::Index":
                    context.Write(output, $"PV::PValue::Machine(");
                    break;
                case "HashMap<&'static str, PV::PValue>":
                    context.Write(output, $"PV::PValue::NamedTuple(");
                    break;
                case "HashMap<i32, PV::PValue>":
                    context.Write(output, $"PV::PValue::Sequence(");
                    break;
                default:
                    break;

            }

            WriteRustExpr(context, output, expr, machine_fields);

            context.Write(output, ")");
        }


        private void WriteRustStmt(CompilationContext context, StringWriter output, Function function,
            IPStmt stmt, string machine_name, IEnumerable<Variable> machine_fields)
        {
            switch (stmt)
            {
                case AnnounceStmt announceStmt:
                    context.Write(output, "currentMachine.Announce((Event)");
                    WriteExpr(context, output, announceStmt.PEvent);
                    if (announceStmt.Payload != null)
                    {
                        context.Write(output, ", ");
                        WriteExpr(context, output, announceStmt.Payload);
                    }

                    context.WriteLine(output, ");");
                    break;

                case AssertStmt assertStmt:
                    context.Write(output, "currentMachine.TryAssert(");
                    WriteExpr(context, output, assertStmt.Assertion);
                    context.Write(output, ",");
                    context.Write(output, $"\"Assertion Failed: \" + ");
                    WriteExpr(context, output, assertStmt.Message);
                    context.WriteLine(output, ");");
                    //last statement
                    if (FunctionValidator.SurelyReturns(assertStmt))
                    {
                        context.WriteLine(output, "throw new PUnreachableCodeException();");
                    }

                    break;

                case AssignStmt assignStmt:
                    WriteRustLValue(context, output, assignStmt.Location, machine_fields);
                    context.Write(output, $" = ");
                    WriteRustExpr(context, output, assignStmt.Value, machine_fields);
                    context.WriteLine(output, ";");
                    break;

                case CompoundStmt compoundStmt:
                    context.WriteLine(output, "{");
                    foreach (IPStmt subStmt in compoundStmt.Statements)
                    {
                        WriteRustStmt(context, output, function, subStmt, machine_name, machine_fields);
                    }

                    context.WriteLine(output, "}");
                    break;

                case CtorStmt ctorStmt:
                    string new_machine_name = InterfaceToName(context.Names.GetNameForDecl(ctorStmt.Interface));

                    context.Write(output,
                        $"self.create_machine(\"{new_machine_name}\", ");
                    if (ctorStmt.Arguments.Any())
                    {
                        context.Write(output, ", ");
                        if (ctorStmt.Arguments.Count > 1)
                        {
                            //create tuple from rvaluelist
                            context.Write(output, "new PrtTuple(");
                            string septor = "";
                            foreach (IPExpr ctorExprArgument in ctorStmt.Arguments)
                            {
                                context.Write(output, septor);
                                WriteExpr(context, output, ctorExprArgument);
                                septor = ",";
                            }

                            context.Write(output, ")");
                        }
                        else
                        {
                            WritePValueExpr(context, output, ctorStmt.Arguments.First(), machine_fields);
                        }
                    }
                    else context.Write(output, "PV::PValue::DefaultVal");

                    context.WriteLine(output, ");");
                    break;

                case FunCallStmt funCallStmt:
                    bool isStatic = funCallStmt.Function.Owner == null;
                    string awaitMethod = funCallStmt.Function.CanReceive == true ? "await " : "";
                    string globalFunctionClass = isStatic ? $"{context.GlobalFunctionClassName}." : "";
                    context.Write(output,
                        $"{awaitMethod}{globalFunctionClass}{context.Names.GetNameForDecl(funCallStmt.Function)}(");
                    string separator = "";

                    foreach (IPExpr param in funCallStmt.ArgsList)
                    {
                        context.Write(output, separator);
                        WriteExpr(context, output, param);
                        separator = ", ";
                    }

                    if (isStatic)
                    {
                        context.Write(output, separator + "currentMachine");
                    }

                    context.WriteLine(output, ");");
                    break;

                case GotoStmt gotoStmt:
                    // last statement
                    // set the execution status to CanExecuteFurther
                    // otherwise defaults to WaitingForEvent
                    context.WriteLine(output, $"self.common_data.current_state = {machine_name}State::{context.Names.GetNameForDecl(gotoStmt.State)};");
                    context.WriteLine(output, "self.common_data.execution_status = MP::ExecutionStatus::CanExecuteFurther;");
                    context.WriteLine(output, "return");
                    break;

                case IfStmt ifStmt:
                    context.Write(output, "if ");
                    WriteRustExpr(context, output, ifStmt.Condition, machine_fields);
                    context.WriteLine(output, "");
                    WriteRustStmt(context, output, function, ifStmt.ThenBranch, machine_name, machine_fields);
                    if (ifStmt.ElseBranch != null && ifStmt.ElseBranch.Statements.Any())
                    {
                        context.WriteLine(output, "else");
                        WriteRustStmt(context, output, function, ifStmt.ElseBranch, machine_name, machine_fields);
                    }

                    break;

                case AddStmt addStmt:
                    context.Write(output, "((PrtSet)");
                    WriteExpr(context, output, addStmt.Variable);
                    context.Write(output, ").Add(");
                    WriteExpr(context, output, addStmt.Value);
                    context.WriteLine(output, ");");
                    break;

                case InsertStmt insertStmt:
                    WriteRustExpr(context, output, insertStmt.Variable, machine_fields);
                    context.Write(output, ".insert(");
                    WriteRustExpr(context, output, insertStmt.Index, machine_fields);
                    context.Write(output, ", ");
                    WritePValueExpr(context, output, insertStmt.Value, machine_fields);
                    context.WriteLine(output, ");");
                    break;

                case MoveAssignStmt moveAssignStmt:
                    string upCast = "";
                    if (!moveAssignStmt.FromVariable.Type.IsSameTypeAs(moveAssignStmt.ToLocation.Type))
                    {
                        upCast = $"({GetCSharpType(moveAssignStmt.ToLocation.Type)})";
                        if (PLanguageType.TypeIsOfKind(moveAssignStmt.FromVariable.Type, TypeKind.Enum))
                        {
                            upCast = $"{upCast}(long)";
                        }
                    }

                    WriteRustLValue(context, output, moveAssignStmt.ToLocation, machine_fields);
                    context.WriteLine(output,
                        $" = {upCast}{context.Names.GetNameForDecl(moveAssignStmt.FromVariable)};");
                    break;

                case NoStmt _:
                    break;

                case PopStmt _:
                    //last statement
                    context.WriteLine(output, "currentMachine.TryPopState();");
                    context.WriteLine(output, "return;");
                    break;

                case PrintStmt printStmt:
                    context.Write(output, $"PModule.runtime.Logger.WriteLine(\"<PrintLog> \" + ");
                    WriteExpr(context, output, printStmt.Message);
                    context.WriteLine(output, ");");
                    break;

                case RaiseStmt raiseStmt:
                    //last statement
                    context.WriteLine(output, "self.common_data.execution_status = MP::ExecutionStatus::Terminated;");
                    context.WriteLine(output, "return");
                    break;

                case ReceiveStmt receiveStmt:
                    string eventName = context.Names.GetTemporaryName("recvEvent");
                    string[] eventTypeNames = receiveStmt.Cases.Keys.Select(evt => context.Names.GetNameForDecl(evt))
                        .ToArray();
                    string recvArgs = string.Join(", ", eventTypeNames.Select(name => $"typeof({name})"));
                    context.WriteLine(output, $"var {eventName} = await currentMachine.TryReceiveEvent({recvArgs});");
                    context.WriteLine(output, $"switch ({eventName}) {{");
                    foreach (KeyValuePair<PEvent, Function> recvCase in receiveStmt.Cases)
                    {
                        string caseName = context.Names.GetTemporaryName("evt");
                        context.WriteLine(output, $"case {context.Names.GetNameForDecl(recvCase.Key)} {caseName}: {{");
                        if (recvCase.Value.Signature.Parameters.FirstOrDefault() is Variable caseArg)
                        {
                            context.WriteLine(output,
                                $"{GetCSharpType(caseArg.Type)} {context.Names.GetNameForDecl(caseArg)} = ({GetCSharpType(caseArg.Type)})({caseName}.Payload);");
                        }

                        foreach (Variable local in recvCase.Value.LocalVariables)
                        {
                            PLanguageType type = local.Type;
                            context.WriteLine(output,
                                $"{GetCSharpType(type, true)} {context.Names.GetNameForDecl(local)} = {DefaultValueForType(type)};");
                        }

                        foreach (IPStmt caseStmt in recvCase.Value.Body.Statements)
                        {
                            WriteStmt(context, output, function, caseStmt);
                        }

                        context.WriteLine(output, "} break;");
                    }

                    context.WriteLine(output, "}");
                    break;

                case RemoveStmt removeStmt:
                    {
                        string castOperation = PLanguageType.TypeIsOfKind(removeStmt.Variable.Type, TypeKind.Map)
                        ? "(PrtMap)"
                        : PLanguageType.TypeIsOfKind(removeStmt.Variable.Type, TypeKind.Sequence)
                        ? "(PrtSeq)"
                        : "(PrtSet)";
                        context.Write(output, $"({castOperation}");
                        switch (removeStmt.Variable.Type.Canonicalize())
                        {
                            case MapType _:
                                WriteExpr(context, output, removeStmt.Variable);
                                context.Write(output, ").Remove(");
                                WriteExpr(context, output, removeStmt.Value);
                                context.WriteLine(output, ");");
                                break;

                            case SequenceType _:
                                WriteExpr(context, output, removeStmt.Variable);
                                context.Write(output, ").RemoveAt(");
                                WriteExpr(context, output, removeStmt.Value);
                                context.WriteLine(output, ");");
                                break;

                            case SetType _:
                                WriteExpr(context, output, removeStmt.Variable);
                                context.Write(output, ").Remove(");
                                WriteExpr(context, output, removeStmt.Value);
                                context.WriteLine(output, ");");
                                break;

                            default:
                                throw new ArgumentOutOfRangeException(
                                    $"Remove cannot be applied to type {removeStmt.Variable.Type.OriginalRepresentation}");
                        }
                        break;
                    }

                case ReturnStmt returnStmt:
                    context.Write(output, "return ");
                    if (returnStmt.ReturnValue != null)
                    {
                        WriteExpr(context, output, returnStmt.ReturnValue);
                    }
                    context.WriteLine(output, ";");
                    break;

                case BreakStmt breakStmt:
                    context.WriteLine(output, "break;");
                    break;

                case ContinueStmt continueStmt:
                    context.WriteLine(output, "continue;");
                    break;

                case SendStmt sendStmt:
                    context.Write(output, "self.send_event(");
                    WriteRustExpr(context, output, sendStmt.MachineExpr, machine_fields);
                    context.Write(output, $", {EventTypeName} {{ name: ");
                    WriteRustExpr(context, output, sendStmt.Evt, machine_fields);

                    context.Write(output, ", payload: ");
                    if (sendStmt.Arguments.Any())
                    {
                        if (sendStmt.Arguments.Count > 1)
                        {
                            //create tuple from rvaluelist
                            string argTypes = string.Join(",",
                                sendStmt.Arguments.Select(a => GetCSharpType(a.Type)));
                            string tupleType = $"PrtTuple";
                            context.Write(output, $"new {tupleType}(");
                            string septor = "";
                            foreach (IPExpr ctorExprArgument in sendStmt.Arguments)
                            {
                                context.Write(output, septor);
                                WriteExpr(context, output, ctorExprArgument);
                                septor = ",";
                            }

                            context.Write(output, ")");
                        }
                        else
                        {
                            WritePValueExpr(context, output, sendStmt.Arguments.First(), machine_fields);
                        }
                    }
                    else
                    {
                        context.Write(output, "PV::PValue::DefaultVal");
                    }

                    context.WriteLine(output, "});");
                    break;

                case SwapAssignStmt swapStmt:
                    throw new NotImplementedException("Swap Assignment Not Implemented");

                case WhileStmt whileStmt:
                    context.Write(output, "while ");
                    WriteRustExpr(context, output, whileStmt.Condition, machine_fields);
                    context.WriteLine(output, "");
                    WriteRustStmt(context, output, function, whileStmt.Body, machine_name, machine_fields);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(stmt));
            }
        }

        private void WriteRustLValue(CompilationContext context, StringWriter output, IPExpr lvalue, IEnumerable<Variable> machine_fields)
        {
#pragma warning disable CCN0002 // Non exhaustive patterns in switch block
            switch (lvalue)
            {
                case MapAccessExpr mapAccessExpr:
                    context.Write(output, "((PrtMap)");
                    WriteLValue(context, output, mapAccessExpr.MapExpr);
                    context.Write(output, ")[");
                    WriteExpr(context, output, mapAccessExpr.IndexExpr);
                    context.Write(output, "]");
                    break;

                case SetAccessExpr setAccessExpr:
                    context.Write(output, "((PrtSet)");
                    WriteLValue(context, output, setAccessExpr.SetExpr);
                    context.Write(output, ")[");
                    WriteExpr(context, output, setAccessExpr.IndexExpr);
                    context.Write(output, "]");
                    break;

                case NamedTupleAccessExpr namedTupleAccessExpr:
                    WriteRustExpr(context, output, namedTupleAccessExpr.SubExpr, machine_fields);
                    context.Write(output, $"[\"{namedTupleAccessExpr.FieldName}\"]");
                    context.Write(output, $"{ExtractionExpr(namedTupleAccessExpr.Entry.Type)}");
                    break;

                case SeqAccessExpr seqAccessExpr:
                    WriteRustLValue(context, output, seqAccessExpr.SeqExpr, machine_fields);
                    context.Write(output, "[&");
                    WriteExpr(context, output, seqAccessExpr.IndexExpr);
                    context.Write(output, "]");
                    context.Write(output, $"{ExtractionExpr(seqAccessExpr.Type)}");
                    break;

                case TupleAccessExpr tupleAccessExpr:
                    context.Write(output, "((PrtTuple)");
                    WriteExpr(context, output, tupleAccessExpr.SubExpr);
                    context.Write(output, $")[{tupleAccessExpr.FieldNo}]");
                    break;

                case VariableAccessExpr variableAccessExpr:
                    Variable var = variableAccessExpr.Variable;
                    if (machine_fields.Contains(var)) context.Write(output, "self.");
                    string varname = context.Names.GetNameForDecl(variableAccessExpr.Variable);
                    context.Write(output, varname);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(lvalue));
            }
#pragma warning restore CCN0002 // Non exhaustive patterns in switch block
        }

        private void WriteRustExpr(CompilationContext context, StringWriter output, IPExpr pExpr, IEnumerable<Variable> machine_fields)
        {
#pragma warning disable CCN0002 // Non exhaustive patterns in switch block
            switch (pExpr)
            {
                case CloneExpr cloneExpr:
                    WriteRustClone(context, output, cloneExpr.Term, machine_fields);
                    break;

                case BinOpExpr binOpExpr:
                    WriteRustExpr(context, output, binOpExpr.Lhs, machine_fields);
                    context.Write(output, $" {BinOpToStr(binOpExpr.Operation)} ");
                    WriteRustExpr(context, output, binOpExpr.Rhs, machine_fields);

                    break;

                case BoolLiteralExpr boolLiteralExpr:
                    context.Write(output, $"{(boolLiteralExpr.Value ? "true" : "false")}");
                    break;

                case CastExpr castExpr:
                    string tp = GetRustType(castExpr.Type);
                    context.Write(output, $"(");
                    WriteRustExpr(context, output, castExpr.SubExpr, machine_fields);
                    context.Write(output, $" as {tp}");
                    context.Write(output, ")");
                    break;

                case CoerceExpr coerceExpr:
                    switch (coerceExpr.Type.Canonicalize())
                    {
                        case PrimitiveType oldType when oldType.IsSameTypeAs(PrimitiveType.Float):
                        case PrimitiveType oldType1 when oldType1.IsSameTypeAs(PrimitiveType.Int):
                            context.Write(output, "(");
                            WriteExpr(context, output, coerceExpr.SubExpr);
                            context.Write(output, ")");
                            break;

                        case PermissionType _:
                            context.Write(output, "(PInterfaces.IsCoercionAllowed(");
                            WriteExpr(context, output, coerceExpr.SubExpr);
                            context.Write(output, ", ");
                            context.Write(output, $"\"I_{coerceExpr.NewType.CanonicalRepresentation}\") ?");
                            context.Write(output, "new PMachineValue(");
                            context.Write(output, "(");
                            WriteExpr(context, output, coerceExpr.SubExpr);
                            context.Write(output, ").Id, ");
                            context.Write(output,
                                $"PInterfaces.GetPermissions(\"I_{coerceExpr.NewType.CanonicalRepresentation}\")) : null)");
                            break;

                        default:
                            throw new ArgumentOutOfRangeException(
                                @"unexpected coercion operation to:" + coerceExpr.Type.CanonicalRepresentation);
                    }

                    break;

                case ChooseExpr chooseExpr:
                    if (chooseExpr.SubExpr == null)
                    {
                        context.Write(output, "((PrtBool)currentMachine.TryRandomBool())");
                    }
                    else
                    {
                        context.Write(output, $"(({GetCSharpType(chooseExpr.Type)})currentMachine.TryRandom(");
                        WriteExpr(context, output, chooseExpr.SubExpr);
                        context.Write(output, $"))");
                    }
                    break;

                case ContainsExpr containsExpr:
                    var isMap = PLanguageType.TypeIsOfKind(containsExpr.Collection.Type, TypeKind.Map);
                    var isSeq = PLanguageType.TypeIsOfKind(containsExpr.Collection.Type, TypeKind.Sequence);
                    var castOp = isMap ? "(PrtMap)"
                        : isSeq ? "(PrtSeq)"
                        : "(PrtSet)";
                    context.Write(output, "((PrtBool)(");
                    context.Write(output, $"({castOp}");
                    WriteExpr(context, output, containsExpr.Collection);
                    if (isMap)
                    {
                        context.Write(output, ").ContainsKey(");
                    }
                    else
                    {
                        context.Write(output, ").Contains(");
                    }

                    WriteExpr(context, output, containsExpr.Item);
                    context.Write(output, ")))");
                    break;

                case CtorExpr ctorExpr:
                    string new_machine_name = InterfaceToName(context.Names.GetNameForDecl(ctorExpr.Interface));

                    context.Write(output,
                        $"self.create_machine(\"{new_machine_name}\", ");
                    if (ctorExpr.Arguments.Any())
                    {
                        if (ctorExpr.Arguments.Count > 1)
                        {
                            //create tuple from rvaluelist
                            context.Write(output, "new PrtTuple(");
                            string septor = "";
                            foreach (IPExpr ctorExprArgument in ctorExpr.Arguments)
                            {
                                context.Write(output, septor);
                                WriteRustExpr(context, output, ctorExprArgument, machine_fields);
                                septor = ",";
                            }

                            context.Write(output, ")");
                        }
                        else
                        {
                            WritePValueExpr(context, output, ctorExpr.Arguments.First(), machine_fields);
                        }
                    }
                    else context.Write(output, "PV::PValue::DefaultVal");

                    context.Write(output, ")");
                    break;

                case DefaultExpr defaultExpr:
                    context.Write(output, DefaultValueForType(defaultExpr.Type));
                    break;

                case EnumElemRefExpr enumElemRefExpr:
                    EnumElem enumElem = enumElemRefExpr.Value;
                    context.Write(output, $"(PrtEnum.Get(\"{context.Names.GetNameForDecl(enumElem)}\"))");
                    break;

                case EventRefExpr eventRefExpr:
                    string eventName = context.Names.GetNameForDecl(eventRefExpr.Value);
                    switch (eventName)
                    {
                        case "Halt":
                            context.Write(output, $"{EventNameTypeName}::PHalt");
                            break;

                        case "DefaultEvent":
                            context.Write(output, $"{EventNameTypeName}::{DefaultEventName}");
                            break;

                        default:
                            // QUES: Why are we taking the default value for type? What about the argument itself
                            context.Write(output, $"{EventNameTypeName}::{eventName}");
                            break;
                    }

                    break;

                case FairNondetExpr _:
                    context.Write(output, "((PrtBool)currentMachine.TryRandomBool())");
                    break;

                case FloatLiteralExpr floatLiteralExpr:
                    context.Write(output, $"((PrtFloat){floatLiteralExpr.Value})");
                    break;

                case FunCallExpr funCallExpr:
                    bool isStatic = funCallExpr.Function.Owner == null;
                    string awaitMethod = funCallExpr.Function.CanReceive == true ? "await " : "";
                    string globalFunctionClass = isStatic ? $"{context.GlobalFunctionClassName}." : "";
                    context.Write(output,
                        $"{awaitMethod}{globalFunctionClass}{context.Names.GetNameForDecl(funCallExpr.Function)}(");
                    string separator = "";

                    foreach (IPExpr param in funCallExpr.Arguments)
                    {
                        context.Write(output, separator);
                        WriteExpr(context, output, param);
                        separator = ", ";
                    }

                    if (isStatic)
                    {
                        context.Write(output, separator + "currentMachine");
                    }

                    context.Write(output, ")");
                    break;

                case IntLiteralExpr intLiteralExpr:
                    context.Write(output, $"{intLiteralExpr.Value}");
                    break;

                case KeysExpr keysExpr:
                    context.Write(output, "(");
                    WriteExpr(context, output, keysExpr.Expr);
                    context.Write(output, ").CloneKeys()");
                    break;

                case LinearAccessRefExpr linearAccessRefExpr:
                    string swapKeyword = linearAccessRefExpr.LinearType.Equals(LinearType.Swap) ? "ref " : "";
                    context.Write(output, $"{swapKeyword}{context.Names.GetNameForDecl(linearAccessRefExpr.Variable)}");
                    break;

                case NamedTupleExpr namedTupleExpr:
                    IEnumerable<string> fieldNamesArray = ((NamedTupleType)namedTupleExpr.Type).Names;
                    context.Write(output, $"PV::PValue::to_hashmap(vec![");
                    int i = 0;
                    foreach (string field in fieldNamesArray)
                    {
                        context.Write(output, $"(\"{field}\", ");
                        WritePValueExpr(context, output, namedTupleExpr.TupleFields[i], machine_fields);
                        context.Write(output, $")");
                        i++;
                        if (i < namedTupleExpr.TupleFields.Count)
                            context.Write(output, $", ");
                    }

                    context.Write(output, "])");
                    break;

                case NondetExpr _:
                    context.Write(output, "((PrtBool)currentMachine.TryRandomBool())");
                    break;

                case NullLiteralExpr _:
                    context.Write(output, "null");
                    break;

                case SizeofExpr sizeofExpr:
                    WriteRustExpr(context, output, sizeofExpr.Expr, machine_fields);
                    context.Write(output, ".len()");
                    context.Write(output, " as i32");
                    break;

                case StringExpr stringExpr:
                    context.Write(output, $"((PrtString) String.Format(");
                    context.Write(output, $"\"{stringExpr.BaseString}\"");
                    foreach (var arg in stringExpr.Args)
                    {
                        context.Write(output, ",");
                        WriteExpr(context, output, arg);
                    }
                    context.Write(output, "))");
                    break;

                case ThisRefExpr _:
                    context.Write(output, "self.common_data.self_id");
                    break;

                case UnaryOpExpr unaryOpExpr:
                    context.Write(output, $"{UnOpToStr(unaryOpExpr.Operation)}(");
                    WriteExpr(context, output, unaryOpExpr.SubExpr);
                    context.Write(output, ")");
                    break;

                case UnnamedTupleExpr unnamedTupleExpr:
                    context.Write(output, $"new {GetCSharpType(unnamedTupleExpr.Type)}(");
                    string sep = "";
                    foreach (IPExpr field in unnamedTupleExpr.TupleFields)
                    {
                        context.Write(output, sep);
                        WriteExpr(context, output, field);
                        sep = ", ";
                    }

                    context.Write(output, ")");
                    break;

                case ValuesExpr valuesExpr:
                    context.Write(output, "(");
                    WriteExpr(context, output, valuesExpr.Expr);
                    context.Write(output, ").CloneValues()");
                    break;

                case MapAccessExpr _:
                case SetAccessExpr _:
                case NamedTupleAccessExpr _:
                case SeqAccessExpr _:
                case TupleAccessExpr _:
                case VariableAccessExpr _:
                    WriteRustLValue(context, output, pExpr, machine_fields);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(pExpr), $"type was {pExpr?.GetType().FullName}");
            }
#pragma warning restore CCN0002 // Non exhaustive patterns in switch block
        }

        private void WriteClone(CompilationContext context, StringWriter output, IExprTerm cloneExprTerm)
        {
            if (!(cloneExprTerm is IVariableRef variableRef))
            {
                WriteExpr(context, output, cloneExprTerm);
                return;
            }

            string varName = context.Names.GetNameForDecl(variableRef.Variable);
            context.Write(output, $"(({GetCSharpType(variableRef.Type)})((IPrtValue){varName})?.Clone())");
        }

        private void WriteRustClone(CompilationContext context, StringWriter output, IExprTerm cloneExprTerm, IEnumerable<Variable> machine_fields)
        {
            WriteRustExpr(context, output, cloneExprTerm, machine_fields);
            
            /*
            if (!(cloneExprTerm is IVariableRef variableRef))
            {
                WriteRustExpr(context, output, cloneExprTerm, machine_fields);
                return;
            }

            string varName = context.Names.GetNameForDecl(variableRef.Variable);
            context.Write(output, $"{varName}");
            */
        }

        private string GetCSharpType(PLanguageType type, bool isVar = false)
        {
            switch (type.Canonicalize())
            {
                case DataType _:
                    return "IPrtValue";

                case EnumType _:
                    return "PrtInt";

                case ForeignType _:
                    return type.CanonicalRepresentation;

                case MapType _:
                    return "PrtMap";

                case NamedTupleType _:
                    return "PrtNamedTuple";

                case PermissionType _:
                    return "PMachineValue";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Any):
                    return "IPrtValue";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Bool):
                    return "PrtBool";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Int):
                    return "PrtInt";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Float):
                    return "PrtFloat";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.String):
                    return "PrtString";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Event):
                    return "PEvent";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Machine):
                    return "PMachineValue";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Null):
                    return isVar ? "IPrtValue" : "void";

                case SequenceType _:
                    return "PrtSeq";

                case SetType _:
                    return "PrtSet";

                case TupleType _:
                    return "PrtTuple";

                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private string GetRustType(PLanguageType type, bool isVar = false)
        {
            switch (type.Canonicalize())
            {
                case DataType _:
                    throw new NotImplementedException();

                case EnumType _:
                    throw new NotImplementedException();

                case ForeignType _:
                    throw new NotImplementedException();

                case MapType _:
                    throw new NotImplementedException();

                case NamedTupleType _:
                    return "HashMap<&'static str, PV::PValue>";

                case PermissionType _:
                    return "M::Index";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Any):
                    throw new NotImplementedException();

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Bool):
                    return "bool";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Int):
                    return "i32";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Float):
                    return "f64";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.String):
                    throw new NotImplementedException();

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Event):
                    throw new NotImplementedException();

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Machine):
                    return "M::Index";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Null):
                    return "";

                case SequenceType _:
                    return "HashMap<i32, PV::PValue>";

                case SetType _:
                    throw new NotImplementedException();

                case TupleType _:
                    throw new NotImplementedException();

                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private string GetDefaultValue(PLanguageType returnType)
        {
            switch (returnType.Canonicalize())
            {
                case EnumType enumType:
                    return $"((PrtInt){enumType.EnumDecl.Values.Min(elem => elem.Value)})";

                case MapType mapType:
                    return $"new {GetCSharpType(mapType)}()";

                case SequenceType sequenceType:
                    return $"new {GetCSharpType(sequenceType)}()";

                case SetType setType:
                    return $"new {GetCSharpType(setType)}()";

                case NamedTupleType namedTupleType:
                    string fieldNamesArray = string.Join(",", namedTupleType.Names.Select(n => $"\"{n}\""));
                    fieldNamesArray = $"new string[]{{{fieldNamesArray}}}";
                    string fieldDefaults =
                        string.Join(", ", namedTupleType.Types.Select(t => GetDefaultValue(t)));
                    return $"(new {GetCSharpType(namedTupleType)}({fieldNamesArray},{fieldDefaults}))";

                case TupleType tupleType:
                    string defaultTupleValues =
                        string.Join(", ", tupleType.Types.Select(t => GetDefaultValue(t)));
                    return $"(new {GetCSharpType(tupleType)}({defaultTupleValues}))";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Bool):
                    return "((PrtBool)false)";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Int):
                    return "((PrtInt)0)";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Float):
                    return "((PrtFloat)0.0)";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.String):
                    return "((PrtString)\"\")";

                case PrimitiveType eventType when eventType.IsSameTypeAs(PrimitiveType.Event):
                case PermissionType _:
                case PrimitiveType anyType when anyType.IsSameTypeAs(PrimitiveType.Any):
                case PrimitiveType machineType when machineType.IsSameTypeAs(PrimitiveType.Machine):
                case ForeignType _:
                case PrimitiveType nullType when nullType.IsSameTypeAs(PrimitiveType.Null):
                case DataType _:
                    return "null";

                case null:
                    return "";

                default:
                    throw new ArgumentOutOfRangeException(nameof(returnType));
            }
        }

        private string DefaultValueForType(PLanguageType returnType)
        {
            switch (returnType.Canonicalize())
            {
                case EnumType enumType:
                    throw new NotImplementedException();

                case MapType mapType:
                    throw new NotImplementedException();

                case SequenceType sequenceType:
                    return "HashMap::new()";

                case SetType setType:
                    throw new NotImplementedException();

                case NamedTupleType namedTupleType:
                    return "HashMap::new()";

                case TupleType tupleType:
                    throw new NotImplementedException();

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Bool):
                    return "false";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Int):
                    return "0";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.Float):
                    return "0.0";

                case PrimitiveType primitiveType when primitiveType.IsSameTypeAs(PrimitiveType.String):
                    return "\"\"";

                case PrimitiveType machineType when machineType.IsSameTypeAs(PrimitiveType.Machine):
                    return "M::Index::dummy_index()";

                case PermissionType _:
                    return "M::Index::dummy_index()";

                case PrimitiveType eventType when eventType.IsSameTypeAs(PrimitiveType.Event):
                    return $"{EventNameTypeName}::{DefaultEventName}";

                case PrimitiveType nullType when nullType.IsSameTypeAs(PrimitiveType.Null):
                    return "null";

                case PrimitiveType anyType when anyType.IsSameTypeAs(PrimitiveType.Any):
                case ForeignType _:
                case DataType _:
                    throw new NotImplementedException();

                case null:
                    return "null";

                default:
                    throw new ArgumentOutOfRangeException(nameof(returnType));
            }
        }

        private static string UnOpToStr(UnaryOpType operation)
        {
            switch (operation)
            {
                case UnaryOpType.Negate:
                    return "-";

                case UnaryOpType.Not:
                    return "!";

                default:
                    throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
            }
        }

        private static string BinOpToStr(BinOpType binOpType)
        {
            switch (binOpType)
            {
                case BinOpType.Add:
                    return "+";

                case BinOpType.Sub:
                    return "-";

                case BinOpType.Mul:
                    return "*";

                case BinOpType.Div:
                    return "/";

                case BinOpType.Lt:
                    return "<";

                case BinOpType.Le:
                    return "<=";

                case BinOpType.Gt:
                    return ">";

                case BinOpType.Ge:
                    return ">=";

                case BinOpType.And:
                    return "&&";

                case BinOpType.Or:
                    return "||";

                case BinOpType.Eq:
                    return "==";

                case BinOpType.Neq:
                    return "!=";

                default:
                    throw new ArgumentOutOfRangeException(nameof(binOpType), binOpType, null);
            }
        }
    }
}