using System.Net;
using JobSearchTracker.Api.Data;
using Npgsql;

namespace JobSearchTracker.Api.Tests;

public class CompanyListImportApiTests : ApiTestBase
{
    private async Task<int> EmployerCountAsync() => (await GetAsync("/api/employers?take=500")).Count;

    private async Task<int> MemberCountAsync(Guid listId) =>
        (await GetAsync($"/api/company-lists/{listId}")).Body.GetProperty("employerCount").GetInt32();

    private static string Status(Reply reply, int index) => reply.Body.GetProperty("rows")[index].GetProperty("status").GetString()!;

    private static int Summary(Reply reply, string name) => reply.Body.GetProperty("summary").GetProperty(name).GetInt32();

    /// <summary>A list with "Acme" in it, and "Globex" as an employer that is in no list.</summary>
    private async Task<(Guid ListId, Guid AcmeId, Guid GlobexId)> SeedAsync()
    {
        var listId = await CreateListAsync("Ahmedabad product companies");
        var acme = await CreateEmployerAsync("Acme");
        var globex = await CreateEmployerAsync("Globex");
        await AddEmployerToListAsync(listId, acme);
        return (listId, acme, globex);
    }

    [Fact]
    public async Task Preview_reports_each_of_the_five_statuses_and_writes_nothing()
    {
        var (listId, acmeId, globexId) = await SeedAsync();
        var text = "Acme\n  GLOBEX \nInitech, Inc.\n\ninitech, inc.\n" + new string('x', 201);

        var reply = await PostAsync($"/api/company-lists/{listId}/import/preview", new { text });

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        var rows = reply.Body.GetProperty("rows");
        Assert.Equal(5, rows.GetArrayLength());
        Assert.Equal(new[] { "already_in_list", "existing_employer", "new", "duplicate_in_input", "invalid" }, Enumerable.Range(0, 5).Select(i => Status(reply, i)));
        Assert.Equal(new[] { 1, 2, 3, 5, 6 }, rows.EnumerateArray().Select(r => r.GetProperty("rowNumber").GetInt32()));
        Assert.Equal(acmeId, rows[0].GetProperty("existingEmployerId").GetGuid());
        Assert.Equal(globexId, rows[1].GetProperty("existingEmployerId").GetGuid());
        Assert.Equal("GLOBEX", rows[1].GetProperty("name").GetString());
        Assert.Equal("globex", rows[1].GetProperty("normalizedName").GetString());
        Assert.Equal("  GLOBEX ", rows[1].GetProperty("input").GetString());
        Assert.Equal("Initech, Inc.", rows[2].GetProperty("name").GetString());
        Assert.Equal(3, rows[3].GetProperty("duplicateOfRow").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, rows[4].GetProperty("name").ValueKind);
        Assert.All(rows.EnumerateArray(), r => Assert.False(string.IsNullOrWhiteSpace(r.GetProperty("message").GetString())));

        Assert.Equal(5, Summary(reply, "total"));
        Assert.Equal(1, Summary(reply, "new"));
        Assert.Equal(1, Summary(reply, "existingEmployer"));
        Assert.Equal(1, Summary(reply, "alreadyInList"));
        Assert.Equal(1, Summary(reply, "duplicateInInput"));
        Assert.Equal(1, Summary(reply, "invalid"));

        // Non-destructive: no employer and no membership was written.
        Assert.Equal(2, await EmployerCountAsync());
        Assert.Equal(1, await MemberCountAsync(listId));
    }

    [Fact]
    public async Task Preview_keeps_commas_that_belong_to_a_company_name()
    {
        var listId = await CreateListAsync("List");

        var reply = await PostAsync($"/api/company-lists/{listId}/import/preview", new { text = "Acme, Inc." });

        Assert.Equal(1, reply.Body.GetProperty("rows").GetArrayLength());
        Assert.Equal("Acme, Inc.", reply.Body.GetProperty("rows")[0].GetProperty("name").GetString());
        Assert.Equal("new", Status(reply, 0));
    }

    [Fact]
    public async Task Preview_matches_an_existing_employer_by_its_normalized_name()
    {
        var (listId, _, globexId) = await SeedAsync();

        var reply = await PostAsync($"/api/company-lists/{listId}/import/preview", new { text = "ＧＬＯＢＥＸ" });

        Assert.Equal("existing_employer", Status(reply, 0));
        Assert.Equal(globexId, reply.Body.GetProperty("rows")[0].GetProperty("existingEmployerId").GetGuid());
    }

    [Fact]
    public async Task Import_reuses_existing_employers_creates_new_ones_and_never_duplicates_memberships()
    {
        var (listId, acmeId, globexId) = await SeedAsync();
        var otherList = await CreateListAsync("Other list");
        await AddEmployerToListAsync(otherList, globexId);

        var reply = await PostAsync($"/api/company-lists/{listId}/import", new { text = "Acme\r\n  GLOBEX \r\nInitech, Inc.\r\n\r\ninitech, inc.\r\nacme" });

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal(1, reply.Body.GetProperty("created").GetInt32()); // Initech, Inc.
        Assert.Equal(1, reply.Body.GetProperty("reused").GetInt32()); // Globex
        Assert.Equal(2, reply.Body.GetProperty("addedToList").GetInt32());
        Assert.Equal(3, reply.Body.GetProperty("skipped").GetInt32()); // Acme already in list, two repeated rows

        Assert.Equal(3, await EmployerCountAsync()); // no second Globex or Acme
        var list = await GetAsync($"/api/company-lists/{listId}");
        var members = list.Body.GetProperty("employers").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(3, members.Length);
        Assert.Contains(acmeId, members);
        Assert.Contains(globexId, members);
        Assert.Equal(1, await MemberCountAsync(otherList)); // other lists are untouched
    }

    [Fact]
    public async Task A_second_identical_import_changes_nothing()
    {
        var listId = await CreateListAsync("List");
        var body = new { text = "Acme\nGlobex\nInitech\nacme" };

        var first = await PostAsync($"/api/company-lists/{listId}/import", body);
        var second = await PostAsync($"/api/company-lists/{listId}/import", body);

        Assert.Equal(3, first.Body.GetProperty("created").GetInt32());
        Assert.Equal(1, first.Body.GetProperty("skipped").GetInt32());
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(0, second.Body.GetProperty("created").GetInt32());
        Assert.Equal(0, second.Body.GetProperty("reused").GetInt32());
        Assert.Equal(0, second.Body.GetProperty("addedToList").GetInt32());
        Assert.Equal(4, second.Body.GetProperty("skipped").GetInt32());
        Assert.Equal(3, await EmployerCountAsync());
        Assert.Equal(3, await MemberCountAsync(listId));
    }

    [Fact]
    public async Task Preview_and_import_agree_on_every_row()
    {
        var (listId, _, _) = await SeedAsync();
        var text = "Acme\nGlobex\nInitech\nglobex\nHooli";

        var preview = await PostAsync($"/api/company-lists/{listId}/import/preview", new { text });
        var import = await PostAsync($"/api/company-lists/{listId}/import", new { text });

        Assert.Equal(Summary(preview, "new"), import.Body.GetProperty("created").GetInt32());
        Assert.Equal(Summary(preview, "existingEmployer"), import.Body.GetProperty("reused").GetInt32());
        Assert.Equal(Summary(preview, "alreadyInList") + Summary(preview, "duplicateInInput"), import.Body.GetProperty("skipped").GetInt32());
    }

    [Theory]
    [InlineData("import/preview")]
    [InlineData("import")]
    public async Task An_unknown_list_is_404(string suffix)
    {
        var reply = await PostAsync($"/api/company-lists/{Guid.NewGuid()}/{suffix}", new { text = "Acme" });

        AssertProblem(reply, HttpStatusCode.NotFound, "not_found");
        Assert.Equal(0, await EmployerCountAsync());
    }

    [Theory]
    [InlineData("import/preview")]
    [InlineData("import")]
    public async Task A_missing_or_blank_text_is_a_400_validation_error(string suffix)
    {
        var listId = await CreateListAsync("List");

        AssertInvalid(await PostAsync($"/api/company-lists/{listId}/{suffix}", new { }), "text");
        AssertInvalid(await PostAsync($"/api/company-lists/{listId}/{suffix}", new { text = (string?)null }), "text");
        AssertInvalid(await PostAsync($"/api/company-lists/{listId}/{suffix}", new { text = "" }), "text");
        AssertInvalid(await PostAsync($"/api/company-lists/{listId}/{suffix}", new { text = " \r\n \t\n" }), "text");
        Assert.Equal(0, await EmployerCountAsync());
    }

    [Theory]
    [InlineData("import/preview")]
    [InlineData("import")]
    public async Task A_malformed_json_body_is_a_400_problem(string suffix)
    {
        var listId = await CreateListAsync("List");

        var reply = await SendRawJsonAsync(HttpMethod.Post, $"/api/company-lists/{listId}/{suffix}", "{ \"text\": ");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Contains("json", reply.ContentType);
        Assert.Equal(0, await EmployerCountAsync());
    }

    [Theory]
    [InlineData("import/preview")]
    [InlineData("import")]
    public async Task More_than_1000_non_blank_rows_is_a_400_and_1000_is_accepted(string suffix)
    {
        var listId = await CreateListAsync("List");
        string Rows(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => $"Company {i}"));

        var over = await PostAsync($"/api/company-lists/{listId}/{suffix}", new { text = Rows(1001) });
        AssertInvalid(over, "text");
        Assert.Equal(0, await EmployerCountAsync());

        var atLimit = await PostAsync($"/api/company-lists/{listId}/{suffix}", new { text = Rows(1000) });
        Assert.Equal(HttpStatusCode.OK, atLimit.Status);
        if (suffix == "import")
        {
            Assert.Equal(1000, atLimit.Body.GetProperty("created").GetInt32());
            Assert.Equal(1000, atLimit.Body.GetProperty("addedToList").GetInt32());
            Assert.Equal(1000, await MemberCountAsync(listId)); // not EmployerCountAsync: that helper returns at most 500
        }
        else
        {
            Assert.Equal(1000, atLimit.Body.GetProperty("rows").GetArrayLength());
            Assert.Equal(1000, Summary(atLimit, "new"));
            Assert.Equal(0, await EmployerCountAsync());
            Assert.Equal(0, await MemberCountAsync(listId));
        }
    }

    [Fact]
    public async Task An_invalid_row_blocks_the_whole_save_and_every_invalid_row_is_reported()
    {
        var listId = await CreateListAsync("List");
        var text = "Valid Co\n" + new string('x', 201) + "\nAlso Valid\n" + new string('y', 250);

        var reply = await PostAsync($"/api/company-lists/{listId}/import", new { text });

        AssertInvalid(reply, "text");
        var messages = reply.Body.GetProperty("errors").GetProperty("text").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(2, messages.Length);
        Assert.StartsWith("Row 2:", messages[0]);
        Assert.StartsWith("Row 4:", messages[1]);
        Assert.Equal(0, await EmployerCountAsync());
        Assert.Equal(0, await MemberCountAsync(listId));
    }

    [Fact]
    public async Task Two_imports_running_at_the_same_time_create_each_employer_and_membership_once()
    {
        var listId = await CreateListAsync("List");
        var text = string.Join("\n", Enumerable.Range(1, 25).Select(i => $"Company {i}"));

        var replies = await Task.WhenAll(
            PostAsync($"/api/company-lists/{listId}/import", new { text }),
            PostAsync($"/api/company-lists/{listId}/import", new { text }));

        Assert.All(replies, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Equal(25, replies.Sum(r => r.Body.GetProperty("created").GetInt32()));
        Assert.Equal(25, await EmployerCountAsync());
        Assert.Equal(25, await MemberCountAsync(listId));
    }

    /// <summary>Inserts an employer in a transaction that the test keeps open, so a later insert of the same name must wait.</summary>
    private static async Task<Guid> InsertUncommittedEmployerAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string name)
    {
        var id = Guid.NewGuid();
        await using var command = new NpgsqlCommand(
            "INSERT INTO employers (\"Id\", \"Name\", \"NormalizedName\") VALUES (@id, @name, @normalized)", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("normalized", NameNormalizer.Normalize(name));
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>
    /// Waits until another session is blocked on a lock (the import waiting for the held insert), or fails after 20 s.
    /// It polls from its own autocommit connection: inside the holder's open transaction PostgreSQL would keep returning the
    /// same pg_stat_activity snapshot, so a backend opened after the first poll (the import's) would never show up.
    /// </summary>
    private async Task WaitUntilAnotherSessionIsBlockedAsync(Task<Reply> import)
    {
        await using var monitor = new NpgsqlConnection(ConnectionString);
        await monitor.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            Assert.False(import.IsCompleted, "The import finished instead of waiting for the held employer insert.");
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid() AND wait_event_type = 'Lock'",
                monitor);
            if ((long)(await command.ExecuteScalarAsync())! > 0)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("The import never blocked on the held employer insert.");
    }

    [Fact]
    public async Task An_import_that_loses_an_employer_name_race_reuses_the_winner()
    {
        var listId = await CreateListAsync("List");
        await using var holder = new NpgsqlConnection(ConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        var globexId = await InsertUncommittedEmployerAsync(holder, transaction, "Globex");

        // The import creates Acme, then blocks on the unique index while "Globex" is held uncommitted.
        var import = PostAsync($"/api/company-lists/{listId}/import", new { text = "Acme\nGlobex\nInitech" });
        await WaitUntilAnotherSessionIsBlockedAsync(import);
        await transaction.CommitAsync();
        var reply = await import.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal(2, reply.Body.GetProperty("created").GetInt32()); // Acme, Initech
        Assert.Equal(1, reply.Body.GetProperty("reused").GetInt32()); // Globex, created by the other session
        Assert.Equal(3, reply.Body.GetProperty("addedToList").GetInt32());
        Assert.Equal(0, reply.Body.GetProperty("skipped").GetInt32());
        Assert.Equal(3, await EmployerCountAsync());
        var list = await GetAsync($"/api/company-lists/{listId}");
        Assert.Contains(globexId, list.Body.GetProperty("employers").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
        Assert.Equal(3, await MemberCountAsync(listId));
    }

    [Fact]
    public async Task An_overlapping_import_that_adds_a_membership_does_not_break_an_import_resolving_an_employer()
    {
        var listId = await CreateListAsync("List");
        await CreateEmployerAsync("Acme"); // exists, but is not in the list yet
        await using var holder = new NpgsqlConnection(ConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        var globexId = await InsertUncommittedEmployerAsync(holder, transaction, "Globex");

        // Import A has read Acme (existing, not in the list) and waits for the held Globex insert.
        var importA = PostAsync($"/api/company-lists/{listId}/import", new { text = "Acme\nGlobex" });
        await WaitUntilAnotherSessionIsBlockedAsync(importA);

        // Import B adds Acme to the list and commits while A is waiting; A's own membership for Acme will now conflict.
        var importB = await PostAsync($"/api/company-lists/{listId}/import", new { text = "Acme" });
        Assert.Equal(HttpStatusCode.OK, importB.Status);
        Assert.Equal(1, importB.Body.GetProperty("addedToList").GetInt32());

        await transaction.CommitAsync();
        var reply = await importA.WaitAsync(TimeSpan.FromSeconds(30));

        // A recovers the name race for Globex, then retries as a whole when its Acme membership conflicts: no 500.
        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal(0, reply.Body.GetProperty("created").GetInt32());
        Assert.Equal(1, reply.Body.GetProperty("reused").GetInt32()); // Globex
        Assert.Equal(1, reply.Body.GetProperty("addedToList").GetInt32());
        Assert.Equal(1, reply.Body.GetProperty("skipped").GetInt32()); // Acme, already added by B
        Assert.Equal(2, await EmployerCountAsync());
        var list = await GetAsync($"/api/company-lists/{listId}");
        Assert.Contains(globexId, list.Body.GetProperty("employers").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
        Assert.Equal(2, await MemberCountAsync(listId));
    }

    [Fact]
    public async Task The_resolve_endpoint_still_returns_201_for_a_new_employer_and_200_for_an_existing_one()
    {
        var created = await PostAsync("/api/employers/resolve", new { name = "  ACME   Corp " });
        var again = await PostAsync("/api/employers/resolve", new { name = "acme corp" });

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal(created.Id, again.Id);
    }
}
