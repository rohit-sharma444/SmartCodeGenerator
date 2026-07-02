using System.Reflection.Emit;
using System.Reflection;
using System.Collections;
using Microsoft.Office.Interop.Excel;
using PROJECTBUILDER.DTO;


namespace PROJECTBUILDER;

/// <summary>
/// It created class dynamically based on different data source and their metadata.
/// </summary>
public class ClassBuilder
{
    private string ClassName { get; set; }

    private AssemblyName ClassAssemblyName = new AssemblyName();

    private List<string> Properties { get; set; }

    private List<Type> PropertiesType { get; set; }

    public ClassBuilder(string name)
    {
        ClassName = name;
        ClassAssemblyName.Name = ClassName;
    }


    protected Type GetDataType(string dType)
    {
        switch (dType)
        {
            case "int":
                return typeof(Int64);
            case "string":
                return typeof(string);
            case "decimal":
                return typeof(decimal);
            case "double":
                return typeof(double);
            case "boolean":
                return typeof(bool);
            case "float":
                return typeof(float);
            case "datetime":
                return typeof(DateTime);
        }
        return null;
    }

    protected List<object> CreateClassList(List<MetaDataDTO> metaData)
    {
        var result = new List<object>();
        result.Add(CreateClassObject(metaData));
        return result;
    }


    protected object? CreateClassObject(List<MetaDataDTO> metaData)
    {
        //if (metaData.Field.Count != Types.Count)
        //{
        //    Console.WriteLine("The number of property names should match their corresopnding types number");
        //}
        TypeBuilder DynamicClass = CreateClass();

        CreateConstructor(DynamicClass);

        foreach (var data in metaData)
        {
            CreateProperty(DynamicClass, data.Field, data.FieldType);
        }

        //for (int ind = 0; ind < PropertyNames.Count(); ind++)
        //    CreateProperty(DynamicClass, PropertyNames[ind], Types[ind]);
        Type type = DynamicClass.CreateType();

        return Activator.CreateInstance(type);
    }

    protected TypeBuilder CreateClass()
    {
        AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(ClassAssemblyName, AssemblyBuilderAccess.Run);
        ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
        TypeBuilder typeBuilder = moduleBuilder.DefineType(ClassAssemblyName.FullName
                            , TypeAttributes.Public |
                            TypeAttributes.Class |
                            TypeAttributes.AutoClass |
                            TypeAttributes.AnsiClass |
                            TypeAttributes.BeforeFieldInit |
                            TypeAttributes.AutoLayout
                            , null);
        return typeBuilder;
    }

    protected void CreateConstructor(TypeBuilder typeBuilder)
    {
        typeBuilder.DefineDefaultConstructor(MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
    }

    protected void CreateProperty(TypeBuilder typeBuilder, string propertyName, Type propertyType)
    {
        FieldBuilder fieldBuilder = typeBuilder.DefineField("_" + propertyName, propertyType, FieldAttributes.Private);

        PropertyBuilder propertyBuilder = typeBuilder.DefineProperty(propertyName, PropertyAttributes.HasDefault, propertyType, null);
        MethodBuilder getPropMthdBldr = typeBuilder.DefineMethod("get_" + propertyName, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, propertyType, Type.EmptyTypes);
        ILGenerator getIl = getPropMthdBldr.GetILGenerator();

        getIl.Emit(OpCodes.Ldarg_0);
        getIl.Emit(OpCodes.Ldfld, fieldBuilder);
        getIl.Emit(OpCodes.Ret);

        MethodBuilder setPropMthdBldr = typeBuilder.DefineMethod("set_" + propertyName,
              MethodAttributes.Public |
              MethodAttributes.SpecialName |
              MethodAttributes.HideBySig,
              null, new[] { propertyType });

        ILGenerator setIl = setPropMthdBldr.GetILGenerator();
        System.Reflection.Emit.Label modifyProperty = setIl.DefineLabel();
        System.Reflection.Emit.Label exitSet = setIl.DefineLabel();

        setIl.MarkLabel(modifyProperty);
        setIl.Emit(OpCodes.Ldarg_0);
        setIl.Emit(OpCodes.Ldarg_1);
        setIl.Emit(OpCodes.Stfld, fieldBuilder);

        setIl.Emit(OpCodes.Nop);
        setIl.MarkLabel(exitSet);
        setIl.Emit(OpCodes.Ret);

        propertyBuilder.SetGetMethod(getPropMthdBldr);
        propertyBuilder.SetSetMethod(setPropMthdBldr);
    }

    protected IList? ListFromType(Type type)
    {
        var listType = typeof(List<>);
        var constructedType = listType.MakeGenericType(type);
        return (Activator.CreateInstance(constructedType) as IList);
    }

    protected string? GetPropertyName(Type tp, string name)
    {
        if (name != null) return tp.GetProperty(name).Name.ToString();
        return null;
    }

    protected object? GetProperty(Type tp, string name)
    {
        if (name != null) return tp.GetProperty(name);
        return null;
    }

    protected void SetValue(PropertyInfo property, object dynClass, object value)
    {
        property.SetValue(dynClass, value);
    }

    protected object? GetValue(object dynclass, Type type, string name)
    {
        if (name == null) return null;
        PropertyInfo? prop1 = GetProperty(type, name) as PropertyInfo;
        return prop1.GetValue(dynclass, null);
    }

    protected int GetExcelDataCount(string fileName)
    {
        Application excel = new Application();
        Workbook wb;
        Worksheet ws;
        wb = excel.Workbooks.Open(fileName);
        ws = (Worksheet)wb.Sheets["Sheet2"];

        var dataCount = ws.UsedRange.Rows.Count;
        wb.Close();
        excel.Quit();
        return dataCount;
    }

}
