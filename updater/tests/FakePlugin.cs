using System;
namespace BepInEx {
    [AttributeUsage(AttributeTargets.Class)]public class BepInPlugin:Attribute {
        public BepInPlugin(string guid,string name,string version){throw new Exception("Plugin attribute must never execute during inventory");}
    }
}
[BepInEx.BepInPlugin("test.inventory","Example Plugin","9.8.7")]
public class ExamplePlugin {static ExamplePlugin(){throw new Exception("Plugin code must never execute");}}
