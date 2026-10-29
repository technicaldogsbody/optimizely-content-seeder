var builder = DistributedApplication.CreateBuilder(args);

var sqlPassword = builder.AddParameter("sql-password", secret: true);

var sql = builder.AddSqlServer("sql", password: sqlPassword, port: 14330)
    .WithDataVolume("contentseeder-demo-sqldata");

// Each demo site has its own database on the shared SQL Server, but both read it as "EPiServerDB".
var db = sql.AddDatabase("EPiServerDB");
var db12 = sql.AddDatabase("EPiServerDB12");

// Mailpit is a local SMTP server that catches every email, so the completion email can be checked without SendGrid.
// SMTP is on 1025 and the web UI is at http://localhost:8025. It needs no login.
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit")
    .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp")
    .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "ui");
var smtp = mailpit.GetEndpoint("smtp");

// Optimizely CMS 13
builder.AddProject<Projects.TechnicalDogsbody_Optimizely_ContentSeeder_Demo>("demo")
    .WithReference(db)
    .WaitFor(db)
    .WaitFor(mailpit)
    .WithEnvironment("ContentSeeder__Smtp__Host", smtp.Property(EndpointProperty.Host))
    .WithEnvironment("ContentSeeder__Smtp__Port", smtp.Property(EndpointProperty.Port))
    .WithEnvironment("ContentSeeder__Smtp__UseStartTls", "false")
    .WithEnvironment("ContentSeeder__Smtp__UserName", "")
    .WithEnvironment("ContentSeeder__Smtp__FromAddress", "content-seeder@example.com");

// Optimizely CMS 12
builder.AddProject<Projects.TechnicalDogsbody_Optimizely_ContentSeeder_Demo12>("demo12")
    .WithReference(db12, connectionName: "EPiServerDB")
    .WaitFor(db12)
    .WaitFor(mailpit)
    .WithEnvironment("ContentSeeder__Smtp__Host", smtp.Property(EndpointProperty.Host))
    .WithEnvironment("ContentSeeder__Smtp__Port", smtp.Property(EndpointProperty.Port))
    .WithEnvironment("ContentSeeder__Smtp__UseStartTls", "false")
    .WithEnvironment("ContentSeeder__Smtp__UserName", "")
    .WithEnvironment("ContentSeeder__Smtp__FromAddress", "content-seeder@example.com");

await builder.Build().RunAsync();
