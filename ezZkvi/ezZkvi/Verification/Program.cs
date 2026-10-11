using System.Reflection;
using System.Security.Claims;
using System.Text;
using ezZkvi.Controllers;
using ezZkvi.Data;
using ezZkvi.Models;
using ezZkvi.Services;
using ezZkvi.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
if (args.Contains("--http-only")) { await HttpChecks.Run(Check); Console.WriteLine($"Completed: {passed} HTTP checks."); return; }
Check(ScoreService.Calculate(0,0)==0, "No simulations");
Check(ScoreService.Calculate(18,18)>0, "Perfect first simulation");
Check(ScoreService.Calculate(900,1000)>ScoreService.Calculate(500,1000)*2, "Accuracy outweighs equal activity");
Check(ScoreService.Calculate(900,1000)>ScoreService.Calculate(18,18), "Many good simulations outrank one perfect simulation");
Check(ScoreService.Calculate(108,118)>ScoreService.Calculate(90,100), "Good result raises Score");
Check(ScoreService.Calculate(90,118)<ScoreService.Calculate(90,100), "Skipped simulation lowers Score");
Check(ScoreService.Calculate(100000,100000)>1000, "Score has no upper cap");
Check(ScoreService.Calculate(0,18)==0, "All skipped score zero");
Check(DifficultyNames.TryParse("Teško", out var hard)&&hard==Tezina.TESKO&&DifficultyNames.Csv(hard)=="Tesko", "Difficulty CSV/display mapping");
foreach(var method in new[]{typeof(ContentController).GetMethod("BulkDelete")!,typeof(FeedbackController).GetMethod("BulkDelete")!,typeof(AdministratorController).GetMethod("UpdateRegistrationPolicy")!}) {
    var auth = method.GetCustomAttribute<AuthorizeAttribute>() ?? method.DeclaringType!.GetCustomAttribute<AuthorizeAttribute>();
    Check(auth?.Roles=="Admin" && method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null, method.Name+" authorization and CSRF");
}

var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").Build();
var cs = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
if (cs.Host is not ("localhost" or "127.0.0.1" or "::1")) throw new Exception("Verification only permits a local PostgreSQL server.");
var dbName = "ezkvi_verify_" + Guid.NewGuid().ToString("N");
cs.Database = "postgres";
await using var admin = new NpgsqlConnection(cs.ConnectionString);
await admin.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"",admin).ExecuteNonQueryAsync();
cs.Database=dbName;
ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs.ConnectionString).Options);
try {
    // First test the data migration against a realistic legacy database.
    await using(var db=Db()) {
        await db.GetService<IMigrator>().MigrateAsync("20260610225046_AddStudentStatistika");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO \"AspNetUsers\" (\"Id\",\"UserName\",\"NormalizedUserName\",\"EmailConfirmed\",\"PhoneNumberConfirmed\",\"TwoFactorEnabled\",\"LockoutEnabled\",\"AccessFailedCount\",\"IsApproved\",\"Discriminator\") VALUES ('legacy','legacy','LEGACY',true,false,false,false,0,true,'Korisnik')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO \"Predmet\" (\"Id\",\"Naziv\") VALUES (1,'DM'),(2,'TP'); INSERT INTO \"Oblast\" (\"Id\",\"Naziv\",\"PredmetId\") VALUES (1,'DM oblast',1),(2,'TP oblast',2)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO \"KvizSesija\" (\"TraziBrojPitanja\",\"VremenskoOgranicenje\",\"Status\",\"StudentId\",\"PredmetId\",\"OblastId\",\"BrojTacnih\",\"Procenat\",\"DatumPocetka\",\"DatumZavrsetka\") SELECT 5,3,1,'legacy',1,1,4,80,NOW()-interval '10 days'+i*interval '1 day',NOW()-interval '10 days'+i*interval '1 day' FROM generate_series(1,7) i");
        await db.Database.MigrateAsync();
        Check(await db.KvizSesije.CountAsync(s=>s.StudentId=="legacy")==5,"Migration keeps latest five legacy results");
        var totals=await db.StudentStatistike.SingleAsync();
        Check(totals.UkupnoPitanja==35&&totals.TacniOdgovori==28&&totals.BrojKvizova==7,"Migration preserves lifetime totals before pruning");
        var latest=await db.KvizSesije.OrderByDescending(s=>s.DatumZavrsetka).FirstAsync();
        Check(Math.Abs(latest.NoviScore-ScoreService.Calculate(28,35))<1e-8,"Migration and C# use identical Score formulas");
        Check(latest.MaksimalniPoeni==0.5m&&latest.BrojNetacnih==null,"Legacy results preserve old maximum and unknown breakdown");
        Check((await db.ApplicationSettings.SingleAsync()).RequireAdminApproval,"Default approval policy enabled");
        db.Users.AddRange(new Korisnik{Id="student",UserName="student",IsApproved=true,EmailConfirmed=true},new Korisnik{Id="other",UserName="other",IsApproved=true,EmailConfirmed=true});
        for(int i=0;i<22;i++) {
            var q=new Pitanje{TekstPitanja="Test pitanje "+i,PredmetId=1,OblastId=1,Tezina=(Tezina)(i%3)};
            db.Pitanje.Add(q);
            for(int j=0;j<4;j++) db.Odgovor.Add(new Odgovor{Pitanje=q,Tekst="Odgovor "+j,IsTacan=j==0});
        }
        await db.SaveChangesAsync();
    }
    T Controller<T>(T controller,string user="student",string role="Student") where T:Controller {
        var http = new DefaultHttpContext();
        http.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user),new Claim(ClaimTypes.Role,role)},"Verification"));
        controller.ControllerContext=new ControllerContext{HttpContext=http};
        controller.TempData=new TempDataDictionary(http,new MemoryTempData());
        return controller;
    }
    var identityServices = new ServiceCollection();
    identityServices.AddLogging();
    identityServices.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
    identityServices.AddDbContext<ApplicationDbContext>(o=>o.UseNpgsql(cs.ConnectionString));
    identityServices.AddIdentity<Korisnik,IdentityRole>(o=>o.SignIn.RequireConfirmedAccount=true)
        .AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
    await using(var provider=identityServices.BuildServiceProvider()) {
        using var scope=provider.CreateScope();
        var manager=scope.ServiceProvider.GetRequiredService<UserManager<Korisnik>>();
        var signIn=scope.ServiceProvider.GetRequiredService<SignInManager<Korisnik>>();
        var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await roles.CreateAsync(new IdentityRole("Student"));
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email=new TestEmail();
        AccountController Account() {
            var c=Controller(new AccountController(signIn,manager,email,db));
            c.HttpContext.Request.Scheme="http";
            c.HttpContext.Request.Host=new HostString("localhost");
            c.HttpContext.RequestServices=scope.ServiceProvider;
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext=c.HttpContext;
            c.Url=new TestUrl(c.ControllerContext);
            return c;
        }
        const string password="Verification1!";
        var registration=Account();
        await registration.Register(new(){Email="pending@example.invalid",Password=password,ConfirmPassword=password});
        var pending=await manager.FindByEmailAsync("pending@example.invalid");
        Check(pending!=null&&!pending.IsApproved&&!pending.EmailConfirmed&&email.Sent==1,"Approval-enabled registration requires both conditions");
        Check(await Account().Login(new(){Email=pending!.Email!,Password=password}) is ViewResult,"Login rejects unverified email");
        var token=await manager.GenerateEmailConfirmationTokenAsync(pending);
        await Account().ConfirmEmail(pending.Id,WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)));
        Check(pending.EmailConfirmed&&!pending.IsApproved,"Email confirmation does not approve pending account");
        Check(await Account().Login(new(){Email=pending.Email!,Password=password}) is ViewResult,"Login rejects unapproved account");
        var adminController=Controller(new AdministratorController(db,manager,email),"student","Admin");
        await adminController.UpdateRegistrationPolicy(false);
        await Account().Register(new(){Email="automatic@example.invalid",Password=password,ConfirmPassword=password});
        var automatic=(await manager.FindByEmailAsync("automatic@example.invalid"))!;
        Check(automatic.IsApproved&&!automatic.EmailConfirmed&&!pending.IsApproved,"Policy disabled only affects new registrations");
        Check(await Account().Login(new(){Email=automatic.Email!,Password=password}) is ViewResult,"Automatically approved account still needs email verification");
        var autoToken=await manager.GenerateEmailConfirmationTokenAsync(automatic);
        await Account().ConfirmEmail(automatic.Id,WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(autoToken)));
        Check(automatic.EmailConfirmed&&automatic.IsApproved,"Disabled policy grants access after email verification");
        await adminController.UpdateRegistrationPolicy(true);
        Check(automatic.IsApproved,"Re-enabling policy does not alter existing accounts");
        await adminController.ApproveUser(pending.Id);
        Check(pending.IsApproved,"Administrator can approve verified account");
    }
    async Task<int> Start() {
        await using var db=Db();
        var controller=Controller(new StudentController(db,new ScoreService(db)));
        var result=await controller.StartSimulation(new SimulacijaKvizaViewModel{PredmetId=1,OblastId=1});
        var model=(SimulacijaKvizaViewModel)((ViewResult)result).Model!;
        Check(model.Questions.Count==18&&model.PreostaloSekundi==900&&model.DeadlineUnixMilliseconds>0,"18 questions and persistent 15-minute deadline");
        return model.KvizSesijaId!.Value;
    }
    await using(var db=Db()) {
        var unavailable=await Controller(new StudentController(db,new ScoreService(db))).StartSimulation(new(){PredmetId=2,OblastId=2});
        Check(unavailable is RedirectToActionResult && !await db.KvizSesije.AnyAsync(s=>s.StudentId=="student"), "Insufficient questions cannot create simulation");
    }
    var first=await Start();
    await using(var db=Db()) {
        var controller=Controller(new StudentController(db,new ScoreService(db)));
        Check(await controller.StartSimulation(new SimulacijaKvizaViewModel{PredmetId=1,OblastId=1}) is RedirectToActionResult,"Second active simulation rejected");
        var resumed=(SimulacijaKvizaViewModel)((ViewResult)await controller.ContinueSimulation(first)).Model!;
        var started=await db.KvizSesije.FindAsync(first);
        Check(resumed.DeadlineUnixMilliseconds==new DateTimeOffset(DateTime.SpecifyKind(started!.DatumPocetka.AddMinutes(15),DateTimeKind.Utc)).ToUnixTimeMilliseconds(), "Resume retains original deadline");
        var items=await db.KvizSesijaPitanja.Where(x=>x.KvizSesijaId==first).OrderBy(x=>x.RedniBroj).ToListAsync();
        var correct=await db.Odgovor.FirstAsync(o=>o.PitanjeId==items[0].PitanjeId&&o.IsTacan);
        await controller.SaveSimulationAnswer(new(){KvizSesijaId=first,PitanjeId=items[0].PitanjeId,OdgovorId=correct.Id});
        var wrong=await db.Odgovor.FirstAsync(o=>o.PitanjeId==items[1].PitanjeId&&!o.IsTacan);
        await controller.SaveSimulationAnswer(new(){KvizSesijaId=first,PitanjeId=items[1].PitanjeId,OdgovorId=wrong.Id});
        await controller.SaveSimulationAnswer(new(){KvizSesijaId=first,PitanjeId=items[2].PitanjeId});
        var session=await db.KvizSesije.FindAsync(first);session!.DatumPocetka=DateTime.UtcNow.AddMinutes(-16);await db.SaveChangesAsync();
        Check(await controller.SaveSimulationAnswer(new(){KvizSesijaId=first,PitanjeId=items[3].PitanjeId}) is BadRequestObjectResult,"Server rejects late answers and finishes expired session");
    }
    await using(var db=Db()) {
        var result=await db.KvizSesije.FindAsync(first);
        Check(result!.Status==StatusSesije.ISTEKAO&&result.BrojTacnih==1&&result.BrojNetacnih==1&&result.BrojPreskocenih==16&&result.OsvojeniPoeni==0.10m&&result.MaksimalniPoeni==2.70m,"Expired simulation decimal scoring and skips");
    }
    for(int i=0;i<3;i++) {
        await using var db=Db();
        var controller=Controller(new StudentController(db,new ScoreService(db)));
        Check(await controller.SimulationResult(first) is ViewResult,"Result can be refreshed");
        Check((await db.StudentStatistike.SingleAsync(s=>s.KorisnikId=="student")).BrojKvizova==1,"Refresh does not count simulation again");
        Check(await Controller(new StudentController(db,new ScoreService(db)),"other").SimulationResult(first) is NotFoundObjectResult,"Other student cannot read result");
    }
    var second=await Start();
    await using(var db=Db()) {
        var controller=Controller(new StudentController(db,new ScoreService(db)));
        var items=await db.KvizSesijaPitanja.Where(x=>x.KvizSesijaId==second).OrderBy(x=>x.RedniBroj).ToListAsync();
        foreach(var item in items) {
            var correct=await db.Odgovor.FirstAsync(o=>o.PitanjeId==item.PitanjeId&&o.IsTacan);
            await controller.SaveSimulationAnswer(new(){KvizSesijaId=second,PitanjeId=item.PitanjeId,OdgovorId=correct.Id});
        }
        await controller.SubmitSimulation(new(){KvizSesijaId=second});
        var session=await db.KvizSesije.FindAsync(second);
        Check(session!.OsvojeniPoeni==2.70m&&session.NoviScore>session.PrethodniScore,"Perfect simulation earns 2.70 and raises Score");
    }
    var third=await Start();
    // Concurrent submissions must update statistics exactly once.
    async Task Submit(int id) { await using var db=Db();await Controller(new StudentController(db,new ScoreService(db))).SubmitSimulation(new(){KvizSesijaId=id}); }
    await Task.WhenAll(Submit(third),Submit(third));
    await using(var db=Db()) {
        var session=await db.KvizSesije.FindAsync(third);
        Check(session!.BrojPreskocenih==18&&session.OsvojeniPoeni==0&&session.NoviScore<session.PrethodniScore,"All skipped simulation decreases Score");
        Check((await db.StudentStatistike.SingleAsync(s=>s.KorisnikId=="student")).BrojKvizova==3,"Concurrent finish is counted once");
    }
    for(int i=0;i<3;i++) await Submit(await Start());
    await using(var db=Db()) {
        Check(await db.KvizSesije.CountAsync(s=>s.StudentId=="student")==5,"Only latest five simulations retained");
        Check(!await db.KvizSesijaPitanja.AnyAsync(s=>s.KvizSesijaId==first),"Old result answer rows deleted");
        var total=await db.StudentStatistike.SingleAsync(s=>s.KorisnikId=="student");
        Check(total.UkupnoPitanja==108&&total.TacniOdgovori==19,"Retention does not reduce lifetime Score totals");
        db.StudentStatistike.Add(new(){KorisnikId="student",PredmetId=2,BrojKvizova=1,UkupnoPitanja=5,TacniOdgovori=5});await db.SaveChangesAsync();
        var score=new ScoreService(db);
        Check(Math.Abs(await score.ForUserAsync("student",1)-ScoreService.Calculate(19,108))<1e-8,"Subject Score excludes other subjects");
        Check(Math.Abs(await score.ForUserAsync("student")-ScoreService.Calculate(24,113))<1e-8,"Overall Score combines counts including old five-question quizzes");
        var dashboard=(DashboardViewModel)((ViewResult)await Controller(new StudentController(db,score)).Dashboard()).Model!;
        var ranking=await score.LeaderboardAsync("student");
        Check(dashboard.Rang==ranking.FindIndex(e=>e.JeTrenutni)+1&&Math.Abs(dashboard.Score-await score.ForUserAsync("student"))<1e-8,"Dashboard and leaderboard agree");
        db.StudentStatistike.AddRange(new(){KorisnikId="other",PredmetId=1,BrojKvizova=6,UkupnoPitanja=108,TacniOdgovori=19},new(){KorisnikId="other",PredmetId=2,BrojKvizova=1,UkupnoPitanja=5,TacniOdgovori=5});
        await db.SaveChangesAsync();
        ranking=await score.LeaderboardAsync("student");
        Check(ranking.FindIndex(e=>e.StudentId=="other")<ranking.FindIndex(e=>e.StudentId=="student") && ranking.Single(e=>e.StudentId=="other").Score==ranking.Single(e=>e.StudentId=="student").Score, "Identical Score uses stable user-ID tie breaker");
    }
    await using(var db=Db()) {
        // Real CSV imports: same text/different answers, exact duplicates and scope isolation.
        var controller=Controller(new PredmetController(db),"student","Admin");
        string header="TekstPitanja,OdgovorA,OdgovorB,OdgovorC,OdgovorD,Tacan,Tezina,Oblast\n";
        string one="\"Koja formula je tačna?\",\"  A, x  \",B,C,D,A,Teško,DM oblast\n";
        string two="\"Koja formula je tačna?\",E,F,G,H,A,Tesko,DM oblast\n";
        async Task Import(int subject,int area,string csv) {
            using var stream=new MemoryStream(Encoding.UTF8.GetBytes(csv));
            await controller.ImportCsv(subject,area,new FormFile(stream,0,stream.Length,"file","test.csv"));
        }
        var before=await db.Pitanje.CountAsync();
        await Import(1,1,header+one+two+one);
        Check(await db.Pitanje.CountAsync()==before+2,"CSV permits different answers and skips exact duplicates within file");
        Check(await db.Odgovor.AnyAsync(o=>o.Tekst=="  A, x  "),"CSV preserves whitespace and commas");
        await Import(2,2,header+one);
        Check(await db.Pitanje.CountAsync()==before+3,"CSV duplicate detection scoped to subject and area");
        await Import(1,1,header+"\"Pitanje sa\nnovim redom\",A,B,C,D,A,Lako,DM oblast\n");
        Check(await db.Pitanje.AnyAsync(p=>p.TekstPitanja=="Pitanje sa\nnovim redom"),"Multiline CSV text retained");
        var export=(FileContentResult)await controller.ExportCsv(1);
        Check(Encoding.UTF8.GetString(export.FileContents).Contains(",Tesko,"),"CSV exports internal difficulty");
    }
    await using(var db=Db()) {
        db.Feedback.AddRange(new Feedback{Sadrzaj="Test feedback jedan",DatumSlanja=DateTime.UtcNow},new Feedback{Sadrzaj="Test feedback dva",DatumSlanja=DateTime.UtcNow});await db.SaveChangesAsync();
        var ids=await db.Feedback.Select(f=>f.ID).ToArrayAsync();
        var controller=Controller(new FeedbackController(db),"student","Admin");
        Check(await controller.BulkDelete(ids,false) is BadRequestResult,"Deletion requires explicit confirmation");
        await controller.BulkDelete(ids,true);
        Check(!await db.Feedback.AnyAsync(),"Bulk feedback deletion persisted");
        var content=Controller(new ContentController(db),"student","Admin");
        var qids=await db.Pitanje.Where(p=>p.TekstPitanja=="Koja formula je tačna?"&&p.PredmetId==1).Select(p=>p.Id).ToArrayAsync();
        await content.BulkDelete("questions",qids,true);
        Check(!await db.Pitanje.AnyAsync(p=>qids.Contains(p.Id))&&!await db.Odgovor.AnyAsync(o=>qids.Contains(o.PitanjeId)),"Bulk questions and answers deleted");
        await content.BulkDelete("areas",new[]{2},true);
        Check(!await db.Oblast.AnyAsync(o=>o.Id==2)&&!await db.Pitanje.AnyAsync(p=>p.OblastId==2),"Bulk area deletion removes questions");
        await content.BulkDelete("subjects",new[]{1,2},true);
        Check(!await db.Predmet.AnyAsync()&&!await db.KvizSesije.AnyAsync()&&!await db.StudentStatistike.AnyAsync(),"Bulk subject deletion removes sessions and aggregate statistics");
    }
    Console.WriteLine($"Completed: {passed} checks passed against isolated PostgreSQL database.");
    if (args.Contains("--serve")) {
        using var identityProvider=identityServices.BuildServiceProvider();
        using var scope=identityProvider.CreateScope();
        var manager=scope.ServiceProvider.GetRequiredService<UserManager<Korisnik>>();
        var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await roles.CreateAsync(new IdentityRole("Admin"));
        var user=new Korisnik{UserName="verification-admin",Email="verification-admin@example.invalid",EmailConfirmed=true,IsApproved=true};
        await manager.CreateAsync(user,"Verification1!");await manager.AddToRoleAsync(user,"Admin");
        await using(var db=Db()) {
            var subject=new Predmet{Naziv="Diskretna matematika"};var area=new Oblast{Naziv="Logika",Predmet=subject};
            db.Predmet.Add(subject);db.Oblast.Add(area);
            for(int i=0;i<22;i++) {
                var q=new Pitanje{TekstPitanja="Koja je formula tačna? Primjer "+(i+1),Predmet=subject,Oblast=area,Tezina=(Tezina)(i%3)};
                db.Pitanje.Add(q);
                for(int j=0;j<4;j++) db.Odgovor.Add(new(){Pitanje=q,Tekst="Odgovor "+(j+1),IsTacan=j==0});
            }
            db.Feedback.Add(new(){Sadrzaj="Primjer feedbacka za provjeru prikaza.",DatumSlanja=DateTime.UtcNow,Predmet=subject});
            await db.SaveChangesAsync();
        }
        var start=new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        start.ArgumentList.Add("bin/Debug/net8.0/ezZkvi.dll");start.ArgumentList.Add("--urls");start.ArgumentList.Add("http://127.0.0.1:5287");
        start.Environment["ConnectionStrings__DefaultConnection"]=cs.ConnectionString;
        start.Environment["ASPNETCORE_ENVIRONMENT"]="Verification";
        using var server=System.Diagnostics.Process.Start(start)!;
        var output=server.StandardOutput.ReadToEndAsync();var errors=server.StandardError.ReadToEndAsync();
        try {
            Console.WriteLine("UI verification server starting at http://127.0.0.1:5287. Create Verification/stop-ui to stop and remove test database.");
            await HttpChecks.Run(Check);
            Console.WriteLine($"Completed: {passed} integration and HTTP checks.");
            while (!File.Exists("Verification/stop-ui") && !server.HasExited) await Task.Delay(500);
            if (server.HasExited) throw new Exception("Verification web server exited: " + await errors);
        } finally {
            if(!server.HasExited) server.Kill(entireProcessTree:true);
            await server.WaitForExitAsync();
            await File.WriteAllTextAsync("Verification/web-server.log", await output + await errors);
        }
    }
} finally {
    NpgsqlConnection.ClearAllPools();
    if (!dbName.StartsWith("ezkvi_verify_",StringComparison.Ordinal)||dbName.Length!=45) throw new Exception("Unexpected test database name; cleanup refused.");
    await new NpgsqlCommand($"DROP DATABASE \"{dbName}\" WITH (FORCE)",admin).ExecuteNonQueryAsync();
}

sealed class MemoryTempData : ITempDataProvider {
    public IDictionary<string,object> LoadTempData(HttpContext context)=>new Dictionary<string,object>();
    public void SaveTempData(HttpContext context,IDictionary<string,object> values) { }
}
sealed class TestEmail : IEmailService {
    public int Sent { get; private set; }
    public Task SendEmailAsync(string toEmail,string subject,string body) { Sent++; return Task.CompletedTask; }
}
sealed class TestUrl(ActionContext context) : IUrlHelper {
    public ActionContext ActionContext=>context;
    public string? Action(UrlActionContext actionContext)=>"http://localhost/Account/ConfirmEmail";
    public string? Content(string? contentPath)=>contentPath;
    public bool IsLocalUrl(string? url)=>true;
    public string? Link(string? routeName,object? values)=>"http://localhost/";
    public string? RouteUrl(UrlRouteContext routeContext)=>"http://localhost/";
}
