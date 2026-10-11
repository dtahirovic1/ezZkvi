using System.Net;
using System.Text.RegularExpressions;

internal static class HttpChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        using var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5287"), Timeout = TimeSpan.FromSeconds(10) };
        HttpResponseMessage? ready = null;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try { ready = await client.GetAsync("/Account/Login"); if (ready.IsSuccessStatusCode) break; }
            catch (HttpRequestException) { }
            await Task.Delay(500);
        }
        check(ready?.StatusCode == HttpStatusCode.OK, "HTTP login page renders");
        ready?.Dispose();
        static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task Login(string email)
        {
            var html = await client.GetStringAsync("/Account/Login");
            using var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>
            {
                ["Email"] = email, ["Password"] = "Verification1!", ["__RequestVerificationToken"] = Token(html)
            }));
            check(response.StatusCode == HttpStatusCode.Redirect, "HTTP authenticated login: " + email);
        }
        using (var anonymous = await client.PostAsync("/Content/BulkDelete", new FormUrlEncodedContent(new Dictionary<string,string>())))
            check(anonymous.StatusCode == HttpStatusCode.Redirect && anonymous.Headers.Location?.OriginalString.Contains("NijeUlogovan") == true,
                "Anonymous bulk deletion requires authentication");
        await Login("verification-admin@example.invalid");
        foreach (var path in new[] { "/Administrator/Dashboard", "/Administrator/Users", "/Administrator/EmailObavijest", "/Content/Index", "/Feedback/Index", "/Leaderboard/Index", "/KvizSesija/Index", "/KvizSesijaPitanje/Index" })
        {
            using var page = await client.GetAsync(path);
            check(page.StatusCode == HttpStatusCode.OK, "HTTP retained administrator page: " + path);
        }
        using (var missing = await client.PostAsync("/Content/BulkDelete", new FormUrlEncodedContent(new Dictionary<string,string>())))
            check(missing.StatusCode == HttpStatusCode.BadRequest, "HTTP rejects missing antiforgery token");
        var content = await client.GetStringAsync("/Content/Index");
        using (var invalid = await client.PostAsync("/Content/BulkDelete", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["__RequestVerificationToken"] = Token(content), ["kind"] = "questions", ["ids"] = "-1", ["confirmed"] = "true"
        }))) check(invalid.StatusCode == HttpStatusCode.BadRequest, "HTTP rejects invalid bulk IDs without deleting data");
        await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = Token(content) }));
        await Login("automatic@example.invalid");
        foreach (var path in new[] { "/Student/Dashboard", "/Student/Prepare", "/Student/Simulate", "/Feedback/Create", "/Leaderboard/Index" })
        {
            using var page = await client.GetAsync(path);
            check(page.StatusCode == HttpStatusCode.OK, "HTTP retained student page: " + path);
        }
        using (var forbidden = await client.GetAsync("/Administrator/Users"))
            check(forbidden.StatusCode == HttpStatusCode.Redirect && forbidden.Headers.Location?.OriginalString.Contains("AccessDenied") == true,
                "Student cannot access administrative controls");
        var dashboard = await client.GetStringAsync("/Student/Dashboard");
        using (var forbidden = await client.PostAsync("/Feedback/Delete/1", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = Token(dashboard) })))
            check(forbidden.StatusCode == HttpStatusCode.Redirect && forbidden.Headers.Location?.OriginalString.Contains("AccessDenied") == true,
                "HTTP feedback deletion restricted to administrator");
        await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = Token(dashboard) }));
        await Login("verification-moderator@example.invalid");
        foreach (var path in new[] { "/Moderator/Dashboard", "/Content/Index", "/Feedback/Index", "/Leaderboard/Index" })
        {
            using var page = await client.GetAsync(path);
            check(page.StatusCode == HttpStatusCode.OK, "HTTP retained moderator page: " + path);
        }
    }
}
