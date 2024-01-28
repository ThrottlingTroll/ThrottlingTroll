# ThrottlingTrollSampleDotNet6InProcDurableFunction

Demonstrates how to use ThrottlingTroll's egress throttling capabilities in a [.NET 6 InProc Azure Function](https://learn.microsoft.com/en-us/azure/azure-functions/functions-dotnet-class-library).

```mermaid
graph LR
HttpHello{{"#32;HttpHello"}}:::function
style HttpHello fill:#D9D9FF,stroke-width:2px
HttpHello.binding0.httpTrigger>"#127760; http:[get,post]"]:::httpTrigger --> HttpHello
HttpHello -.-> HttpHello.binding1.http(["#32;http"]):::http
TestOrchestration_HttpStart{{"#32;TestOrchestration_HttpStart"}}:::function
style TestOrchestration_HttpStart fill:#D9D9FF,stroke-width:2px
TestOrchestration_HttpStart.binding0.httpTrigger>"#127760; http:[get,post]"]:::httpTrigger --> TestOrchestration_HttpStart
TestOrchestration_HttpStart.binding2.durableClient(["#32;durableClient"]):::durableClient -.-> TestOrchestration_HttpStart
TestOrchestration_HttpStart -.-> TestOrchestration_HttpStart.binding1.http(["#32;http"]):::http
SayHello[/"#32;SayHello"/]:::activity
style SayHello fill:#D9D9FF,stroke-width:2px
TestOrchestration ---> SayHello
TestOrchestration[["#32;TestOrchestration"]]:::orchestrator
style TestOrchestration fill:#D9D9FF,stroke-width:2px
TestOrchestration_HttpStart ---> TestOrchestration
```

Activity Function uses a ThrottlingTroll-equipped HttpClient instance to make API calls. That HttpClient instance is configured to make no more than 1 request per 5 seconds. When that limit is exceeded, it will automatically wait _without_ making the actual call. Orchestration execution timeline therefore typically looks like this:
```mermaid
gantt 
title TestOrchestration(2e89fdfaadc14cdca6f01fd406c8ec98) 
dateFormat YYYY-MM-DDTHH:mm:ss.SSS 
axisFormat %H:%M:%S 
(26s24ms):  2023-08-27T18:52:14.592, 26024.3306ms 
SayHello (2s834ms): done, 2023-08-27T18:52:18.693, 2834.0239ms 
SayHello (6s38ms): done, 2023-08-27T18:52:18.694, 6038.0683ms 
SayHello (11s276ms): done, 2023-08-27T18:52:18.694, 11275.5493ms 
SayHello (16s470ms): done, 2023-08-27T18:52:18.694, 16470.4312ms 
SayHello (21s673ms): done, 2023-08-27T18:52:18.694, 21672.5332ms 
```

## How to run locally

As a prerequisite, you will need [Azure Functions Core Tools globally installed](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local#install-the-azure-functions-core-tools).

If you run this code on a GitHub Codespaces instance, then everything (including Redis server) should be pre-installed and ready for you.

1. (Optional, if you want to use **RedisCounterStore**) Add `RedisConnectionString` setting to [local.settings.json](https://github.com/ThrottlingTroll/ThrottlingTroll/blob/main/samples/ThrottlingTrollSampleDotNet6InProcDurableFunction/local.settings.json) file. For a local Redis server that connection string usually looks like `localhost:6379`. 

2. Open your terminal in `samples/ThrottlingTrollSampleDotNet6InProcDurableFunction` folder and type the following:
```
func start
```
