# ThrottlingTroll

Yet another take on rate limiting/throttling in ASP.NET Core.

Install from NuGet:
```
dotnet add package ThrottlingTroll
```

## How to use

Make sure you call one or another form of `.UseThrottlingTroll()` method at startup:
```
public static void Main(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    // .....

    var app = builder.Build();

    app.UseThrottlingTroll();

    // .....
}
```

[All different ways of configuring, all other features and more code snippets are documented in our Wiki](https://github.com/ThrottlingTroll/ThrottlingTroll/wiki).

## Samples

[Here is a separate repo with sample projects, that demonstrates how to use ThrottlingTroll in ASP.NET Core](https://github.com/ThrottlingTroll/ThrottlingTroll-AspDotNetCore-Samples).

