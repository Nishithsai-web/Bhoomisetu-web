using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bhoomisetu.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Bhoomisetu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ApiFlowTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void RequireDatabase()
    {
        if (!fixture.Available)
        {
            Assert.Skip($"Set {ApiFixture.EnvironmentVariable} to run database integration tests.");
        }
    }

    [Fact]
    public async Task Health_endpoints_report_ready()
    {
        RequireDatabase();
        var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready", Ct)).StatusCode);
    }

    [Fact]
    public async Task Every_mapped_column_exists_in_the_database()
    {
        RequireDatabase();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand("SELECT table_name || '.' || column_name FROM information_schema.columns WHERE table_schema = 'public'", connection);
            await using var reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                columns.Add(reader.GetString(0));
            }
        }

        var options = new DbContextOptionsBuilder<BhoomisetuDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var db = new BhoomisetuDbContext(options);
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName()!;
            foreach (var property in entity.GetProperties())
            {
                var column = $"{table}.{property.GetColumnName()}";
                Assert.True(columns.Contains(column), $"Mapped column {column} does not exist.");
            }
        }
    }

    [Fact]
    public async Task Phone_sign_in_issues_a_token_and_records_the_check()
    {
        RequireDatabase();
        var (client, userId) = await fixture.SignInAsync("98480 11111");

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct);
        Assert.Equal(userId, me.GetProperty("id").GetGuid());
        Assert.Equal("+91******1111", me.GetProperty("phoneMasked").GetString());
        Assert.Contains("phone_otp", me.GetProperty("checks").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public async Task Signing_in_twice_keeps_the_same_user()
    {
        RequireDatabase();
        var (_, first) = await fixture.SignInAsync("9848022222");
        var (_, second) = await fixture.SignInAsync("+91 98480 22222");
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Wrong_code_is_rejected_with_a_stable_error_code()
    {
        RequireDatabase();
        var client = fixture.CreateClient();
        await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone = "9848033333" }, Ct);
        var code = fixture.Sms.CodeFor("+919848033333");
        var wrong = code == "000000" ? "111111" : "000000";

        var response = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { phone = "9848033333", code = wrong }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("otp.invalid", (await response.Content.ReadFromJsonAsync<JsonElement>(, Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Invalid_phone_is_rejected()
    {
        RequireDatabase();
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/v1/auth/otp/request", new { phone = "12345" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_parcel_requires_sign_in()
    {
        RequireDatabase();
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/v1/parcels", NewParcel("900"), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Same_land_cannot_be_created_twice()
    {
        RequireDatabase();
        var (client, _) = await fixture.SignInAsync("9848044444");

        var created = await client.PostAsJsonAsync("/api/v1/parcels", NewParcel("901/A"), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(, Ct)).GetProperty("id").GetGuid();

        var duplicate = await client.PostAsJsonAsync("/api/v1/parcels", NewParcel("0901-a", village: "  mokila "), Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>(, Ct);
        Assert.Equal("parcel.already_exists", problem.GetProperty("code").GetString());
        Assert.Equal(id, problem.GetProperty("existingParcelId").GetGuid());
    }

    [Fact]
    public async Task Parcel_shows_area_in_every_unit_and_road_facts_with_source()
    {
        RequireDatabase();
        var (client, _) = await fixture.SignInAsync("9848055555");
        var created = await client.PostAsJsonAsync("/api/v1/parcels", NewParcel("902", areaValue: 2, areaUnit: "acre"), Ct);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(, Ct)).GetProperty("id").GetGuid();

        var parcel = await client.GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{id}", Ct);
        var units = parcel.GetProperty("area").GetProperty("conversions").GetProperty("units");
        Assert.Equal(80m, units.GetProperty("gunta").GetDecimal());
        Assert.Equal(9680m, units.GetProperty("square_yard").GetDecimal());
        var road = parcel.GetProperty("road");
        Assert.Equal(9m, road.GetProperty("widthM").GetDecimal());
        Assert.Equal("measured_on_map", road.GetProperty("widthSource").GetString());
        Assert.Equal(29.5m, road.GetProperty("widthFeet").GetDecimal());
    }

    [Fact]
    public async Task Road_width_without_source_is_rejected()
    {
        RequireDatabase();
        var (client, _) = await fixture.SignInAsync("9848066666");
        var body = NewParcel("903") with { Road = new RoadBody(9, null, "village_road", "direct_public_road", 100) };
        var response = await client.PostAsJsonAsync("/api/v1/parcels", body, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("road.width_source_required", (await response.Content.ReadFromJsonAsync<JsonElement>(, Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Comparison_ranks_agents_and_respects_owner_confirmation()
    {
        RequireDatabase();
        var (ownerClient, ownerId) = await fixture.SignInAsync("9848077771");
        var (agentAClient, agentAId) = await fixture.SignInAsync("9848077772");
        var (agentBClient, _) = await fixture.SignInAsync("9848077773");
        var (reviewerClient, reviewerId) = await fixture.SignInAsync("9848077774");

        var created = await agentAClient.PostAsJsonAsync("/api/v1/parcels", NewParcel("904", areaValue: 1, areaUnit: "acre"), Ct);
        var parcelId = (await created.Content.ReadFromJsonAsync<JsonElement>(, Ct)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Created, (await agentAClient.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/offers",
            new { listerKind = "agent", askingPriceRupees = 5_000_000m, buyerBrokeragePercent = 1m }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await agentBClient.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/offers",
            new { listerKind = "agent", askingPriceRupees = 4_900_000m }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ownerClient.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/offers",
            new { listerKind = "owner", askingPriceRupees = 5_100_000m }, Ct)).StatusCode);

        var before = await GetComparison(parcelId);
        Assert.Equal(3, before.GetProperty("offers").GetArrayLength());
        Assert.All(before.GetProperty("offers").EnumerateArray(), o => Assert.Equal("not_confirmed", o.GetProperty("authority").GetString()));
        Assert.Equal(4840m, before.GetProperty("areaSquareYards").GetDecimal());

        // A non-reviewer cannot set the owner.
        var forbidden = await ownerClient.PostAsJsonAsync($"/api/v1/admin/parcels/{parcelId}/owner-of-record", new { userId = ownerId, evidenceNote = "self" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Promote the reviewer, sign in again to get the role claim, record the owner.
        await fixture.ExecuteSqlAsync("UPDATE users SET is_trust_reviewer = true WHERE id = @id", ("id", reviewerId));
        (reviewerClient, _) = await fixture.SignInAsync("9848077774");
        var setOwner = await reviewerClient.PostAsJsonAsync($"/api/v1/admin/parcels/{parcelId}/owner-of-record",
            new { userId = ownerId, evidenceNote = "Pahani and sale deed checked against Bhu Bharati portal printout" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, setOwner.StatusCode);

        // The owner confirms agent A only.
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.PostAsync($"/api/v1/parcels/{parcelId}/agents/{agentAId}/confirm", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agentBClient.PostAsync($"/api/v1/parcels/{parcelId}/agents/{agentAId}/confirm", null, Ct)).StatusCode);

        var after = await GetComparison(parcelId);
        var byAuthority = after.GetProperty("offers").EnumerateArray().ToDictionary(o => o.GetProperty("authority").GetString()!, o => o);
        Assert.True(byAuthority.ContainsKey("owner_of_record"));
        Assert.True(byAuthority.ContainsKey("owner_confirmed_agent"));
        Assert.True(byAuthority.ContainsKey("not_confirmed"));
        Assert.Equal(1, after.GetProperty("offers")[0].GetProperty("rank").GetInt32());
        Assert.Contains("best_value", after.GetProperty("offers")[0].GetProperty("badges").EnumerateArray().Select(b => b.GetString()));

        // Agent B's undisclosed brokerage is assumed at 2%: ₹49,00,000 + ₹98,000.
        var agentB = byAuthority["not_confirmed"];
        Assert.True(agentB.GetProperty("brokerageAssumed").GetBoolean());
        Assert.Equal(4_998_000m, agentB.GetProperty("effectivePriceRupees").GetDecimal());

        // Lowest-price sort is by effective price.
        var lowest = await GetComparison(parcelId, "lowest_price");
        var prices = lowest.GetProperty("offers").EnumerateArray().Select(o => o.GetProperty("effectivePriceRupees").GetDecimal()).ToList();
        Assert.Equal(prices.Order().ToList(), prices);

        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.CreateClient().GetAsync($"/api/v1/parcels/{parcelId}/comparison?sort=cheapest", Ct)).StatusCode);
    }

    [Fact]
    public async Task New_offer_replaces_the_listers_previous_offer()
    {
        RequireDatabase();
        var (client, _) = await fixture.SignInAsync("9848088888");
        var created = await client.PostAsJsonAsync("/api/v1/parcels", NewParcel("905"), Ct);
        var parcelId = (await created.Content.ReadFromJsonAsync<JsonElement>(, Ct)).GetProperty("id").GetGuid();

        await client.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/offers", new { listerKind = "agent", askingPriceRupees = 1_000_000m, buyerBrokeragePercent = 1m }, Ct);
        await client.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/offers", new { listerKind = "agent", askingPriceRupees = 950_000m, buyerBrokeragePercent = 1m }, Ct);

        var comparison = await GetComparison(parcelId);
        var offer = Assert.Single(comparison.GetProperty("offers").EnumerateArray());
        Assert.Equal(950_000m, offer.GetProperty("askingPriceRupees").GetDecimal());
    }

    [Fact]
    public async Task Owner_offer_cannot_carry_brokerage()
    {
        RequireDatabase();
        var (client, _) = await fixture.SignInAsync("9848099999");
        var created = await client.PostAsJsonAsync("/api/v1/parcels", NewParcel("906"), Ct);
        var parcelId = (await created.Content.ReadFromJsonAsync<JsonElement>(, Ct)).GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/offers", new { listerKind = "owner", askingPriceRupees = 1_000_000m, buyerBrokeragePercent = 1m }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Units_endpoint_converts_gajams()
    {
        RequireDatabase();
        var result = await fixture.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/units/area?value=200&unit=square_yard", Ct);
        Assert.Equal(1800m, result.GetProperty("units").GetProperty("square_foot").GetDecimal());
        Assert.Equal(25m, result.GetProperty("units").GetProperty("ankanam").GetDecimal());
    }

    [Fact]
    public async Task Owner_claim_counts_only_after_review_and_owner_confirms_agent_by_offer()
    {
        RequireDatabase();
        var (claimant, claimantId) = await fixture.SignInAsync("9848011112");
        var (agent, _) = await fixture.SignInAsync("9848011113");
        var created = await claimant.PostAsJsonAsync("/api/v1/parcels", NewParcel("907"), Ct);
        var parcelId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        await claimant.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/offers", new { listerKind = "owner", askingPriceRupees = 1_000_000m }, Ct);
        var agentOffer = await agent.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/offers", new { listerKind = "agent", askingPriceRupees = 990_000m, buyerBrokeragePercent = 1m }, Ct);
        var agentOfferId = (await agentOffer.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        // Before review the "owner" is priced like an agent (2% assumed) and gets no "no brokerage" badge.
        var claim = (await GetComparison(parcelId)).GetProperty("offers").EnumerateArray().Single(o => o.GetProperty("listerKind").GetString() == "owner");
        Assert.True(claim.GetProperty("ownerClaimUnverified").GetBoolean());
        Assert.Equal(1_020_000m, claim.GetProperty("effectivePriceRupees").GetDecimal());
        Assert.DoesNotContain("no_brokerage", claim.GetProperty("badges").EnumerateArray().Select(b => b.GetString()));

        // A reviewer records the owner; the owner then confirms the agent through the agent's offer.
        var (reviewer, reviewerId) = await fixture.SignInAsync("9848011114");
        await fixture.ExecuteSqlAsync("UPDATE users SET is_trust_reviewer = true WHERE id = @id", ("id", reviewerId));
        (reviewer, _) = await fixture.SignInAsync("9848011114");
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.PostAsJsonAsync($"/api/v1/admin/parcels/{parcelId}/owner-of-record",
            new { userId = claimantId, evidenceNote = "1-B and sale deed reviewed" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await claimant.PostAsync($"/api/v1/parcels/{parcelId}/offers/{agentOfferId}/confirm-lister", null, Ct)).StatusCode);

        var offers = (await GetComparison(parcelId)).GetProperty("offers").EnumerateArray().ToList();
        var owner = offers.Single(o => o.GetProperty("listerKind").GetString() == "owner");
        Assert.False(owner.GetProperty("ownerClaimUnverified").GetBoolean());
        Assert.Equal(1_000_000m, owner.GetProperty("effectivePriceRupees").GetDecimal());
        Assert.Equal("owner_confirmed_agent", offers.Single(o => o.GetProperty("listerKind").GetString() == "agent").GetProperty("authority").GetString());

        // Removing the reviewer flag takes effect at once, even with a still-valid token.
        await fixture.ExecuteSqlAsync("UPDATE users SET is_trust_reviewer = false WHERE id = @id", ("id", reviewerId));
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.PostAsJsonAsync($"/api/v1/admin/parcels/{parcelId}/owner-of-record",
            new { userId = claimantId, evidenceNote = "again" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Same_land_with_and_without_village_code_is_one_parcel()
    {
        RequireDatabase();
        var (client, _) = await fixture.SignInAsync("9848011115");
        var first = await client.PostAsJsonAsync("/api/v1/parcels", NewParcel("908") with { LgdVillageCode = "574123" }, Ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await client.PostAsJsonAsync("/api/v1/parcels", NewParcel("908"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Search_matches_village_mandal_or_district()
    {
        RequireDatabase();
        var (client, _) = await fixture.SignInAsync("9848011116");
        await client.PostAsJsonAsync("/api/v1/parcels", NewParcel("909", village: "Kondakal"), Ct);

        foreach (var q in new[] { "kondakal", "Shankarpalle", "rangareddy" })
        {
            var page = await fixture.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/parcels?q={q}", Ct);
            Assert.True(page.GetProperty("total").GetInt32() >= 1, $"no result for {q}");
        }

        var huge = await fixture.CreateClient().GetAsync("/api/v1/parcels?page=2147483647&pageSize=100", Ct);
        Assert.Equal(HttpStatusCode.OK, huge.StatusCode);
    }

    [Theory]
    [InlineData("0.001", "square_metre")]
    [InlineData("10000000000", "square_foot")]
    public async Task Areas_the_database_cannot_hold_are_rejected_not_crashed(string value, string unit)
    {
        RequireDatabase();
        var (client, _) = await fixture.SignInAsync("9848011117");
        var body = NewParcel("910") with { Area = new AreaBody(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), unit) };
        var response = await client.PostAsJsonAsync("/api/v1/parcels", body, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Units_endpoint_rejects_overflow_instead_of_crashing()
    {
        RequireDatabase();
        var response = await fixture.CreateClient().GetAsync("/api/v1/units/area?value=79228162514264337593543950335&unit=acre", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task My_listings_show_offers_and_one_tap_updates_work()
    {
        RequireDatabase();
        var (client, _) = await fixture.SignInAsync("9848022221");
        var created = await client.PostAsJsonAsync("/api/v1/parcels", NewParcel("911"), Ct);
        var parcelId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await client.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/offers", new { listerKind = "agent", askingPriceRupees = 2_500_000m, buyerBrokeragePercent = 1m }, Ct);

        var mine = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/offers", Ct);
        var offer = Assert.Single(mine.EnumerateArray());
        Assert.Equal("active", offer.GetProperty("status").GetString());
        Assert.False(offer.GetProperty("needsConfirmation").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/parcels/{parcelId}/offers/mine/still-available", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/parcels/{parcelId}/offers/mine/sold", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/parcels/{parcelId}/offers/mine/sold", null, Ct)).StatusCode);

        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/offers", Ct);
        Assert.Equal("sold", Assert.Single(after.EnumerateArray()).GetProperty("status").GetString());
        Assert.Empty((await GetComparison(parcelId)).GetProperty("offers").EnumerateArray());
    }

    [Fact]
    public async Task Corrections_are_requested_by_involved_people_and_decided_by_another_reviewer()
    {
        RequireDatabase();
        var (lister, _) = await fixture.SignInAsync("9848022222");
        var (stranger, _) = await fixture.SignInAsync("9848022223");
        var created = await lister.PostAsJsonAsync("/api/v1/parcels", NewParcel("912"), Ct);
        var parcelId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var body = new { field = "road_width_m", proposedValue = "12.2", reason = "Panchayat widened the road last year" };
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/change-requests", body, Ct)).StatusCode);

        var requested = await lister.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/change-requests", body, Ct);
        Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
        var request = await requested.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("9", request.GetProperty("currentValue").GetString());
        Assert.Equal("12.2", request.GetProperty("proposedValue").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await lister.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/change-requests", body, Ct)).StatusCode);

        var bad = new { field = "survey_number", proposedValue = "1", reason = "typo" };
        Assert.Equal(HttpStatusCode.BadRequest, (await lister.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/change-requests", bad, Ct)).StatusCode);

        var (reviewer, reviewerId) = await fixture.SignInAsync("9848022224");
        await fixture.ExecuteSqlAsync("UPDATE users SET is_trust_reviewer = true WHERE id = @id", ("id", reviewerId));
        (reviewer, _) = await fixture.SignInAsync("9848022224");

        var queue = await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/admin/change-requests", Ct);
        Assert.Contains(queue.EnumerateArray(), r => r.GetProperty("parcelId").GetGuid() == parcelId);

        var requestId = request.GetProperty("id").GetGuid();
        var decided = await reviewer.PostAsJsonAsync($"/api/v1/admin/change-requests/{requestId}/decision", new { decision = "approve", note = "Checked on satellite map" }, Ct);
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await reviewer.PostAsJsonAsync($"/api/v1/admin/change-requests/{requestId}/decision", new { decision = "reject" }, Ct)).StatusCode);

        var parcel = await lister.GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}", Ct);
        Assert.Equal(12.2m, parcel.GetProperty("road").GetProperty("widthM").GetDecimal());
    }

    private async Task<JsonElement> GetComparison(Guid parcelId, string sort = "best_value") =>
        await fixture.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}/comparison?sort={sort}", Ct);

    private static ParcelBody NewParcel(string survey, string village = "Mokila", decimal areaValue = 300, string areaUnit = "square_yard") =>
        new("TG", "Rangareddy", "Shankarpalle", village, null, survey, "non_agricultural", new AreaBody(areaValue, areaUnit), "lister",
            null, null, "east", new RoadBody(9, "measured_on_map", "layout_road", "direct_public_road", 250));

    private sealed record AreaBody(decimal Value, string Unit);

    private sealed record RoadBody(decimal? WidthM, string? WidthSource, string? Type, string? AccessRight, int? DistanceToMainRoadM);

    private sealed record ParcelBody(string StateCode, string District, string Mandal, string Village, string? LgdVillageCode, string SurveyNumber,
        string LandClass, AreaBody Area, string AreaSource, decimal? FrontageM, decimal? DepthM, string? Facing, RoadBody? Road);
}
