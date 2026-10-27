var builder = DistributedApplication.CreateBuilder(args);

var sqlPassword = builder.AddParameter("sql-password", secret: true);

var sql = builder.AddSqlServer("sql", password: sqlPassword, port: 14330)
    .WithDataVolume("contentseeder-demo-sqldata");

// Each demo site has its own database on the shared SQL Server, but both read it as "EPiServerDB".
var db = sql.AddDatabase("EPiServerDB");
var db12 = sql.AddDatabase("EPiServerDB12");

// Optimizely CMS 13
builder.AddProject<Projects.TechnicalDogsbody_Optimizely_ContentSeeder_Demo>("demo")
    .WithReference(db)
    .WaitFor(db);

// Optimizely CMS 12
builder.AddProject<Projects.TechnicalDogsbody_Optimizely_ContentSeeder_Demo12>("demo12")
    .WithReference(db12, connectionName: "EPiServerDB")
    .WaitFor(db12);

await builder.Build().RunAsync();
