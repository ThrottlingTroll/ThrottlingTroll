# ThrottlingTroll.CounterStores.EfCore

[ICounterStore](https://github.com/ThrottlingTroll/ThrottlingTroll/blob/main/ThrottlingTroll.Core/CounterStores/ICounterStore.cs) 
implementation based on Entity Framework Core. Intended to work with [any relational database supported by it](https://learn.microsoft.com/en-us/ef/core/providers/?tabs=dotnet-core-cli#current-providers).

Uses transactions, and therefore is subjected to locks. Use with caution, especially under high loads.

## How to use

1. Create a table for storing counters:

   ```
    CREATE TABLE ThrottlingTrollCounters
    (
      [Id] [nvarchar](255) PRIMARY KEY,
      [Count] [bigint] NOT NULL,
      [ExpiresAt] [datetimeoffset] NOT NULL
    )    
   ```

3. Install package from NuGet:

   ```
     dotnet add package ThrottlingTroll.CounterStores.EfCore
   ```
    
5. Install the desired database provider. E.g. for SQL Server:

    ```
      dotnet add package Microsoft.EntityFrameworkCore.SqlServer --version 6.0.31
    ```

2. EITHER put **EfCoreCounterStore** instance into your DI container:

    ```
      builder.Services.AddSingleton<ICounterStore>(
          new EfCoreCounterStore(efConfig =>
          {
              efConfig.UseSqlServer("my-sql-server-connection-string");
          })
      );
    ```
     OR provide an instance of it via **.UseThrottlingTroll()** method:

    ```
      app.UseThrottlingTroll(options =>
      {
          options.CounterStore = new EfCoreCounterStore(efConfig =>
          {
              efConfig.UseSqlServer("my-sql-server-connection-string");
          });
      });
    ```

  **EfCoreCounterStore**'s ctor takes a routine for configuring Entity Framework Core with the desired database provider. The above sample code uses SQL Server, so change it accordingly, if needed.
  
