namespace MinimalApi;

public static class Endpoints
{
    public static string Hello()
    {
        return Greeting("world");
    }

    public static string Echo(string text)
    {
        return text;
    }

    private static string Greeting(string name)
    {
        return "Hello, " + name;
    }
}
