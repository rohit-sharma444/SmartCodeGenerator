namespace CodeGenerationEngine.Models;

/// <summary>The kind of type a create command declares.</summary>
public enum DeclaredKind { Class, Record, Struct, Enum }

/// <summary>The one modifier a create command puts on a type.</summary>
public enum TypeModifier { None, Abstract, Sealed, Static }
