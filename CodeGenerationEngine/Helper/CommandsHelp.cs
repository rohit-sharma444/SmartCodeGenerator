namespace CodeGenerationEngine.Helper;

public static class CommandsHelp
{
    public static bool PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("======================================================");
        Console.WriteLine("Code Generator v1.0");
        Console.WriteLine("======================================================");

        Console.WriteLine();
        Console.WriteLine("GENERAL");
        Console.WriteLine("------------------------------------------------------");
        Console.WriteLine("help");
        Console.WriteLine("help project");
        Console.WriteLine("help class");
        Console.WriteLine("help validation");
        Console.WriteLine("demo");
        Console.WriteLine("exit");

        Console.WriteLine();
        Console.WriteLine("PROJECT CREATION");
        Console.WriteLine("------------------------------------------------------");
        Console.WriteLine("create console project MyApp");
        Console.WriteLine("create class library CommonLib");
        Console.WriteLine("create webapi project TradingApi");
        Console.WriteLine("create blazor project Dashboard");
        Console.WriteLine("create worker service BackgroundJobs");
        Console.WriteLine("create wpf project DesktopApp");

        Console.WriteLine();
        Console.WriteLine("PROJECT MANAGEMENT");
        Console.WriteLine("------------------------------------------------------");
        Console.WriteLine("list projects");
        Console.WriteLine("use project <project name>");
        Console.WriteLine("use project 1");
        Console.WriteLine("current project");

        Console.WriteLine();
        Console.WriteLine("CLASS MANAGEMENT");
        Console.WriteLine("------------------------------------------------------");
        Console.WriteLine("create class <class name>");
        Console.WriteLine("create class <class name> with properties Id:int, Name:string");
        Console.WriteLine("list classes");
        Console.WriteLine("list classes in <project name>");
        Console.WriteLine("show class <class name>");
        Console.WriteLine("delete class <class name>");
        Console.WriteLine("restore class <class name>");
        Console.WriteLine("list deleted classes");
        Console.WriteLine("rename class <existing class name> to <new class name>");

        Console.WriteLine();
        Console.WriteLine("RECYCLE BIN");
        Console.WriteLine("------------------------------------------------------");
        Console.WriteLine("delete class <class name>");
        Console.WriteLine("restore class <class name>");
        Console.WriteLine("restore last class");
        Console.WriteLine("list deleted classes");
        Console.WriteLine("show deleted classes");

        Console.WriteLine();
        Console.WriteLine("PROPERTY MANAGEMENT");
        Console.WriteLine("------------------------------------------------------");
        Console.WriteLine("add property Salary:decimal to Employee");
        Console.WriteLine("add property Email:string to Customer");
        Console.WriteLine("list properties Employee");
        Console.WriteLine("show properties Employee");

        Console.WriteLine();
        Console.WriteLine("METHOD MANAGEMENT");
        Console.WriteLine("------------------------------------------------------");
        Console.WriteLine("add method <method name>");
        Console.WriteLine("list methods <class name>");

        Console.WriteLine();
        Console.WriteLine("SERVICE GENERATION");
        Console.WriteLine("------------------------------------------------------");
        Console.WriteLine("create service EmployeeService");
        Console.WriteLine("create repository EmployeeRepository");
        Console.WriteLine("create interface IEmployeeService");

        Console.WriteLine();
        Console.WriteLine("API GENERATION");
        Console.WriteLine("------------------------------------------------------");
        Console.WriteLine("create controller EmployeeController");
        Console.WriteLine("create api for employee");
        Console.WriteLine("create endpoint get employees");

        Console.WriteLine();
        Console.WriteLine("VALIDATION");
        Console.WriteLine("------------------------------------------------------");
        Console.WriteLine("show validation rules");
        Console.WriteLine("reload validation rules");
        Console.WriteLine("validate project");
        Console.WriteLine("validate class Employee");

        Console.WriteLine();
        Console.WriteLine("ANALYSIS");
        Console.WriteLine("------------------------------------------------------");
        Console.WriteLine("analyze project");
        Console.WriteLine("analyze class Employee");
        Console.WriteLine("generate project report");

        Console.WriteLine();
        Console.WriteLine("CODE SEARCH");
        Console.WriteLine("------------------------------------------------------");
        Console.WriteLine("find class Employee");
        Console.WriteLine("find service EmployeeService");
        Console.WriteLine("find repository EmployeeRepository");

        Console.WriteLine();
        Console.WriteLine("EXAMPLES");
        Console.WriteLine("------------------------------------------------------");
        Console.WriteLine("create webapi project TradingApi");
        Console.WriteLine("use project TradingApi");
        Console.WriteLine("create class Employee with properties Id:int, Name:string");
        Console.WriteLine("list classes");
        Console.WriteLine("current project");

        Console.WriteLine();
        Console.WriteLine("======================================================");
        Console.WriteLine();

        return true;
    }
}
