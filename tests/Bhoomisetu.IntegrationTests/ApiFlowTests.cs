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
    public async Task A_second_code_within_the_hour_needs_the_human_check()
    {
        RequireDatabase();
        var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone = "9848055551" }, Ct)).StatusCode);

        var missing = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone = "9848055551" }, Ct);
        Assert.Equal("captcha.required", (await missing.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
        var wrong = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone = "9848055551", captchaToken = "nope" }, Ct);
        Assert.Equal("captcha.invalid", (await wrong.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
        var passed = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone = "9848055551", captchaToken = FakeCaptcha.PassToken }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, passed.StatusCode);
    }

    [Fact]
    public async Task Owner_sees_their_role_and_confirms_or_removes_agents_by_offer()
    {
        RequireDatabase();
        var (owner, ownerId) = await fixture.SignInAsync("9848055552");
        var (agent, _) = await fixture.SignInAsync("9848055553");
        var created = await agent.PostAsJsonAsync("/api/v1/parcels", NewParcel("914"), Ct);
        var parcelId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var offer = await agent.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/offers", new { listerKind = "agent", askingPriceRupees = 3_000_000m, buyerBrokeragePercent = 1m }, Ct);
        var offerId = (await offer.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var before = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}/viewer", Ct);
        Assert.False(before.GetProperty("isOwnerOfRecord").GetBoolean());

        var (reviewer, reviewerId) = await fixture.SignInAsync("9848055554");
        await fixture.ExecuteSqlAsync("UPDATE users SET is_trust_reviewer = true WHERE id = @id", ("id", reviewerId));
        (reviewer, _) = await fixture.SignInAsync("9848055554");

        // The reviewer finds the owner by mobile number, then records them.
        var found = await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/admin/users/by-phone?phone=98480%2055552", Ct);
        Assert.Equal(ownerId, found.GetProperty("id").GetGuid());
        Assert.Equal("+91******5552", found.GetProperty("phoneMasked").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync("/api/v1/admin/users/by-phone?phone=9000000999", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/admin/users/by-phone?phone=9848055552", Ct)).StatusCode);
        await reviewer.PostAsJsonAsync($"/api/v1/admin/parcels/{parcelId}/owner-of-record", new { userId = ownerId, evidenceNote = "1-B checked" }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/v1/parcels/{parcelId}/offers/{offerId}/confirm-lister", null, Ct)).StatusCode);
        var after = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}/viewer", Ct);
        Assert.True(after.GetProperty("isOwnerOfRecord").GetBoolean());
        Assert.Equal(offerId, Assert.Single(after.GetProperty("confirmedOfferIds").EnumerateArray()).GetGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/parcels/{parcelId}/offers/{offerId}/confirm-lister", Ct)).StatusCode);
        var removed = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}/viewer", Ct);
        Assert.Empty(removed.GetProperty("confirmedOfferIds").EnumerateArray());

        // The agent is not the owner and sees no confirmations.
        var agentView = await agent.GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}/viewer", Ct);
        Assert.False(agentView.GetProperty("isOwnerOfRecord").GetBoolean());
        Assert.Equal(offerId, agentView.GetProperty("myOfferId").GetGuid());
    }

    [Fact]
    public async Task Signing_out_ends_the_session_on_the_server()
    {
        RequireDatabase();
        var (client, _) = await fixture.SignInAsync("9848033334");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/sign-out", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me", Ct)).StatusCode);
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
        Assert.Equal("otp.invalid", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
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
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var duplicate = await client.PostAsJsonAsync("/api/v1/parcels", NewParcel("0901-a", village: "  mokila "), Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("parcel.already_exists", problem.GetProperty("code").GetString());
        Assert.Equal(id, problem.GetProperty("existingParcelId").GetGuid());
    }

    [Fact]
    public async Task Parcel_shows_area_in_every_unit_and_road_facts_with_source()
    {
        RequireDatabase();
        var (client, _) = await fixture.SignInAsync("9848055555");
        var created = await client.PostAsJsonAsync("/api/v1/parcels", NewParcel("902", areaValue: 2, areaUnit: "acre"), Ct);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

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
        Assert.Equal("road.width_source_required", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
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
        var parcelId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

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
        var parcelId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

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
        var parcelId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

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

    [Fact]
    public async Task Set_aside_duplicates_are_hidden_and_a_reviewer_can_merge_them()
    {
        RequireDatabase();
        var (lister, listerId) = await fixture.SignInAsync("9848044441");
        var (owner, ownerId) = await fixture.SignInAsync("9848044443");
        var created = await lister.PostAsJsonAsync("/api/v1/parcels", NewParcel("913", village: "Janwada"), Ct);
        var keptId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        // Simulate what migration 0003 does with land that was listed twice before it ran.
        var duplicateId = Guid.NewGuid();
        await fixture.ExecuteSqlAsync(
            """
            INSERT INTO parcels (id, parcel_key, name_key, state_code, district, mandal, village, survey_number_raw, survey_number,
                                 land_class, recorded_area_sqm, entered_area_value, entered_area_unit, area_source, created_by)
            SELECT @dup, 'TG|lgd:999|913', name_key || '|duplicate-of:' || id, state_code, district, mandal, village, survey_number_raw,
                   survey_number, land_class, recorded_area_sqm, entered_area_value, entered_area_unit, area_source, created_by
            FROM parcels WHERE id = @kept;
            INSERT INTO parcel_duplicate_candidates (parcel_id, kept_parcel_id, name_key) SELECT @dup, @kept, name_key FROM parcels WHERE id = @kept;
            INSERT INTO offers (parcel_id, lister_user_id, lister_kind, asking_price_paise) VALUES (@dup, @lister, 'agent', 100000000);
            INSERT INTO offers (parcel_id, lister_user_id, lister_kind, asking_price_paise, created_at)
                VALUES (@kept, @lister, 'agent', 90000000, now() - interval '2 days');
            INSERT INTO parcel_parties (parcel_id, user_id, role, confirmed_by) VALUES (@dup, @owner, 'owner_of_record', @lister);
            """,
            ("dup", duplicateId), ("kept", keptId), ("lister", listerId), ("owner", ownerId));

        // Writes to a hidden duplicate are sent to the kept record instead of being stranded.
        var stranded = await lister.PostAsJsonAsync($"/api/v1/parcels/{duplicateId}/offers", new { listerKind = "agent", askingPriceRupees = 1_100_000m, buyerBrokeragePercent = 1m }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stranded.StatusCode);
        var strandedProblem = await stranded.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("parcel.duplicate", strandedProblem.GetProperty("code").GetString());
        Assert.Equal(keptId, strandedProblem.GetProperty("existingParcelId").GetGuid());

        var search = await fixture.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/parcels?q=janwada", Ct);
        Assert.Equal(1, search.GetProperty("total").GetInt32());
        var compareDuplicate = await fixture.CreateClient().GetAsync($"/api/v1/parcels/{duplicateId}/comparison", Ct);
        Assert.Equal(HttpStatusCode.Conflict, compareDuplicate.StatusCode);

        var (reviewer, reviewerId) = await fixture.SignInAsync("9848044442");
        await fixture.ExecuteSqlAsync("UPDATE users SET is_trust_reviewer = true WHERE id = @id", ("id", reviewerId));
        (reviewer, _) = await fixture.SignInAsync("9848044442");
        var queue = await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/admin/duplicates", Ct);
        Assert.Contains(queue.EnumerateArray(), d => d.GetProperty("parcelId").GetGuid() == duplicateId && d.GetProperty("activeOffers").GetInt32() == 1);
        var merged = await reviewer.PostAsync($"/api/v1/admin/duplicates/{duplicateId}/merge", null, Ct);
        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);

        // The lister's newer price (on the duplicate) wins over the older one on the kept record; the owner moves across.
        var offers = (await GetComparison(keptId)).GetProperty("offers").EnumerateArray().ToList();
        Assert.Single(offers);
        Assert.Equal(1_000_000m, offers[0].GetProperty("askingPriceRupees").GetDecimal());
        var viewer = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{keptId}/viewer", Ct);
        Assert.True(viewer.GetProperty("isOwnerOfRecord").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.PostAsync($"/api/v1/admin/duplicates/{duplicateId}/merge", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_reviewer_with_a_stake_cannot_merge_and_account_lookups_are_limited()
    {
        RequireDatabase();
        var (reviewer, reviewerId) = await fixture.SignInAsync("9848066661");
        await fixture.ExecuteSqlAsync("UPDATE users SET is_trust_reviewer = true WHERE id = @id", ("id", reviewerId));
        (reviewer, _) = await fixture.SignInAsync("9848066661");

        // The reviewer listed the kept land, so the merge is left to someone else.
        var created = await reviewer.PostAsJsonAsync("/api/v1/parcels", NewParcel("917", village: "Kollur"), Ct);
        var keptId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var duplicateId = Guid.NewGuid();
        await fixture.ExecuteSqlAsync(
            """
            INSERT INTO parcels (id, parcel_key, name_key, state_code, district, mandal, village, survey_number_raw, survey_number,
                                 land_class, recorded_area_sqm, entered_area_value, entered_area_unit, area_source, created_by)
            SELECT @dup, 'TG|lgd:998|917', name_key || '|duplicate-of:' || id, state_code, district, mandal, village, survey_number_raw,
                   survey_number, land_class, recorded_area_sqm, entered_area_value, entered_area_unit, area_source, created_by
            FROM parcels WHERE id = @kept;
            INSERT INTO parcel_duplicate_candidates (parcel_id, kept_parcel_id, name_key) SELECT @dup, @kept, name_key FROM parcels WHERE id = @kept;
            """,
            ("dup", duplicateId), ("kept", keptId));
        var merge = await reviewer.PostAsync($"/api/v1/admin/duplicates/{duplicateId}/merge", null, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, merge.StatusCode);
        Assert.Equal("merge.reviewer_involved", (await merge.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());

        // After the hourly allowance of lookups, the next one is refused.
        await fixture.ExecuteSqlAsync(
            "INSERT INTO audit_log (actor_user_id, action, entity_type, entity_id) SELECT @id, 'admin.user_lookup', 'user', 'none' FROM generate_series(1, 30)",
            ("id", reviewerId));
        var lookup = await reviewer.GetAsync("/api/v1/admin/users/by-phone?phone=9848066662", Ct);
        Assert.Equal((HttpStatusCode)429, lookup.StatusCode);
        Assert.Equal("admin.lookup_limit", (await lookup.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Subscribed_listers_add_public_documents_that_buyers_open_after_review_and_contact_them_on_whatsapp()
    {
        RequireDatabase();
        var (lister, listerId) = await fixture.SignInAsync("9848077771");
        var (buyer, _) = await fixture.SignInAsync("9848077772");
        var (reviewer, reviewerId) = await fixture.SignInAsync("9848077773");
        await fixture.ExecuteSqlAsync("UPDATE users SET is_trust_reviewer = true WHERE id = @id", ("id", reviewerId));
        (reviewer, _) = await fixture.SignInAsync("9848077773");

        var created = await lister.PostAsJsonAsync("/api/v1/parcels", NewParcel("931", village: "Gopanpally"), Ct);
        var parcelId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var offer = await lister.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/offers", new { listerKind = "agent", askingPriceRupees = 4_000_000m, buyerBrokeragePercent = 1m }, Ct);
        var offerId = (await offer.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var brochure = "%PDF-1.7\nBhoomisetu test brochure"u8.ToArray();

        // Without a subscription: no uploads and no WhatsApp contact.
        Assert.Equal("subscription.lister_required", await CodeOf(await Upload(lister, parcelId, "brochure", brochure)));
        Assert.Equal("contact.lister_not_subscribed", await CodeOf(await buyer.GetAsync($"/api/v1/parcels/{parcelId}/offers/{offerId}/contact", Ct)));

        // A reviewer records the payment; a reviewer cannot activate their own.
        var activate = await reviewer.PostAsJsonAsync("/api/v1/admin/subscriptions",
            new { phone = "98480 77771", audience = "lister", months = 1, amountRupees = 499m, paymentReference = "UPI-123456" }, Ct);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        Assert.Equal("subscription.self", await CodeOf(await reviewer.PostAsJsonAsync("/api/v1/admin/subscriptions",
            new { phone = "9848077773", audience = "lister", months = 1, amountRupees = 0m, paymentReference = "x" }, Ct)));
        var status = await lister.GetFromJsonAsync<JsonElement>("/api/v1/me/subscription", Ct);
        Assert.True(status.GetProperty("listerActive").GetBoolean());

        // One payment, one period: the reference cannot be reused, and UPI IDs or phone numbers are refused.
        Assert.Equal("subscription.reference_used", await CodeOf(await reviewer.PostAsJsonAsync("/api/v1/admin/subscriptions",
            new { phone = "9848077772", audience = "viewer", months = 1, amountRupees = 99m, paymentReference = "upi-123456" }, Ct)));
        Assert.Equal("subscription.reference_invalid", await CodeOf(await reviewer.PostAsJsonAsync("/api/v1/admin/subscriptions",
            new { phone = "9848077772", audience = "viewer", months = 1, amountRupees = 99m, paymentReference = "someone@ybl" }, Ct)));
        Assert.Equal("subscription.reference_invalid", await CodeOf(await reviewer.PostAsJsonAsync("/api/v1/admin/subscriptions",
            new { phone = "9848077772", audience = "viewer", months = 1, amountRupees = 99m, paymentReference = "+91 9848012345" }, Ct)));
        Assert.Equal("subscription.reference_invalid", await CodeOf(await reviewer.PostAsJsonAsync("/api/v1/admin/subscriptions",
            new { phone = "9848077772", audience = "viewer", months = 1, amountRupees = 99m, paymentReference = "98480 12345" }, Ct)));
        Assert.Equal("subscription.reference_used", await CodeOf(await reviewer.PostAsJsonAsync("/api/v1/admin/subscriptions",
            new { phone = "9848077772", audience = "viewer", months = 1, amountRupees = 0m, paymentReference = "UPI 123456" }, Ct)));

        // A renewal follows on from the current period, and "active until" shows the later date (30-day months).
        var firstEnd = status.GetProperty("listerUntil").GetDateTimeOffset();
        Assert.Equal(HttpStatusCode.OK, (await reviewer.PostAsJsonAsync("/api/v1/admin/subscriptions",
            new { phone = "9848077771", audience = "lister", months = 2, amountRupees = 998m, paymentReference = "UTR-998877" }, Ct)).StatusCode);
        var renewed = (await lister.GetFromJsonAsync<JsonElement>("/api/v1/me/subscription", Ct)).GetProperty("listerUntil").GetDateTimeOffset();
        Assert.Equal(firstEnd.AddDays(60), renewed);

        // The comparison now marks this lister as reachable on WhatsApp.
        Assert.True((await GetComparison(parcelId)).GetProperty("offers")[0].GetProperty("contactable").GetBoolean());

        // Only allowed types, a real file, the number for approvals, and the declaration.
        Assert.Equal("document.type_invalid", await CodeOf(await Upload(lister, parcelId, "sale_deed", brochure)));
        Assert.Equal("document.format", await CodeOf(await Upload(lister, parcelId, "brochure", "MZ fake exe"u8.ToArray())));
        Assert.Equal("document.reference_required", await CodeOf(await Upload(lister, parcelId, "rera_certificate", brochure)));
        Assert.Equal("document.declaration_required", await CodeOf(await Upload(lister, parcelId, "brochure", brochure, declaration: false)));
        var uploaded = await Upload(lister, parcelId, "brochure", brochure);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var documentId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        Assert.Equal("document.already_added", await CodeOf(await Upload(lister, parcelId, "brochure", brochure)));
        Assert.Equal("document.not_involved", await CodeOf(await Upload(buyer, parcelId, "brochure", "%PDF-other"u8.ToArray())));

        // Before review: visitors see nothing, buyers cannot open it, the lister sees it as pending.
        var visitor = await fixture.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}/documents", Ct);
        Assert.Equal("sign_in", visitor.GetProperty("access").GetString());
        Assert.Empty(visitor.GetProperty("documents").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync($"/api/v1/parcels/{parcelId}/documents/{documentId}/file", Ct)).StatusCode);
        var own = await lister.GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}/documents", Ct);
        Assert.Equal("pending", own.GetProperty("documents")[0].GetProperty("status").GetString());
        Assert.True(own.GetProperty("canUpload").GetBoolean());

        // Review: a rejection needs a reason; approval makes it visible to signed-in buyers (viewer plan is free).
        Assert.Contains((await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/admin/documents", Ct)).EnumerateArray(), d => d.GetProperty("id").GetGuid() == documentId);
        Assert.Equal("document.note_required", await CodeOf(await reviewer.PostAsJsonAsync($"/api/v1/admin/documents/{documentId}/decision", new { decision = "reject" }, Ct)));
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.PostAsJsonAsync($"/api/v1/admin/documents/{documentId}/decision", new { decision = "approve" }, Ct)).StatusCode);

        var forBuyer = await buyer.GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}/documents", Ct);
        Assert.Equal("full", forBuyer.GetProperty("access").GetString());
        Assert.Single(forBuyer.GetProperty("documents").EnumerateArray());
        var file = await buyer.GetAsync($"/api/v1/parcels/{parcelId}/documents/{documentId}/file", Ct);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("application/pdf", file.Content.Headers.ContentType!.MediaType);
        Assert.Equal(brochure, await file.Content.ReadAsByteArrayAsync(Ct));
        var counts = (await fixture.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}/documents", Ct)).GetProperty("available");
        Assert.Equal(1, counts[0].GetProperty("count").GetInt32());

        // Search results carry the lowest asking price and the reviewed-document count, and filter by land type.
        var found = (await fixture.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/parcels?q=gopanpally", Ct)).GetProperty("items")[0];
        Assert.Equal(4_040_000m, found.GetProperty("lowestPriceRupees").GetDecimal()); // asking price plus 1% brokerage
        Assert.Equal(1, found.GetProperty("reviewedDocuments").GetInt32());
        Assert.False(found.GetProperty("ownerOfRecordRecorded").GetBoolean());
        var detail = await fixture.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}", Ct);
        Assert.False(detail.GetProperty("ownerOfRecordRecorded").GetBoolean());
        var landClass = found.GetProperty("landClass").GetString();
        var other = landClass == "agricultural" ? "converted" : "agricultural";
        Assert.Single((await fixture.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/parcels?q=gopanpally&landClass={landClass}", Ct)).GetProperty("items").EnumerateArray());
        Assert.Empty((await fixture.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/parcels?q=gopanpally&landClass={other}", Ct)).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.CreateClient().GetAsync("/api/v1/parcels?landClass=moon", Ct)).StatusCode);

        // WhatsApp: the sign-in number by default, or the number the lister chose.
        var contact = await buyer.GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}/offers/{offerId}/contact", Ct);
        Assert.Equal("+919848077771", contact.GetProperty("whatsappE164").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await lister.PutAsJsonAsync("/api/v1/me/whatsapp", new { phone = "9000077771" }, Ct)).StatusCode);
        contact = await buyer.GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}/offers/{offerId}/contact", Ct);
        Assert.Equal("+919000077771", contact.GetProperty("whatsappE164").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.CreateClient().GetAsync($"/api/v1/parcels/{parcelId}/offers/{offerId}/contact", Ct)).StatusCode);

        // The lister removes the document; it disappears for everyone.
        Assert.Equal(HttpStatusCode.NoContent, (await lister.DeleteAsync($"/api/v1/parcels/{parcelId}/documents/{documentId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync($"/api/v1/parcels/{parcelId}/documents/{documentId}/file", Ct)).StatusCode);
    }

    [Fact]
    public async Task Reviewers_add_sourced_developments_to_land_and_handle_listing_reports()
    {
        RequireDatabase();
        var (lister, _) = await fixture.SignInAsync("9848088881");
        var (buyer, _) = await fixture.SignInAsync("9848088882");
        var (reviewer, reviewerId) = await fixture.SignInAsync("9848088883");
        await fixture.ExecuteSqlAsync("UPDATE users SET is_trust_reviewer = true WHERE id = @id", ("id", reviewerId));
        (reviewer, _) = await fixture.SignInAsync("9848088883");
        var created = await lister.PostAsJsonAsync("/api/v1/parcels", NewParcel("941", village: "Kandi"), Ct);
        var parcelId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        // A non-proposed project needs an https source; dates cannot be in the future.
        Assert.Equal("development.source_required", await CodeOf(await reviewer.PostAsJsonAsync("/api/v1/admin/developments",
            new { name = "NH-65 widening", kind = "road_widening", status = "approved", authority = "NHAI", sourceDate = "2026-01-10" }, Ct)));
        Assert.Equal("development.source_invalid", await CodeOf(await reviewer.PostAsJsonAsync("/api/v1/admin/developments",
            new { name = "NH-65 widening", kind = "road_widening", status = "approved", authority = "NHAI", sourceUrl = "javascript:alert(1)", sourceDate = "2026-01-10" }, Ct)));
        Assert.Equal("development.date_invalid", await CodeOf(await reviewer.PostAsJsonAsync("/api/v1/admin/developments",
            new { name = "Metro phase 2", kind = "metro", status = "proposed_unverified", authority = "HMRL", sourceDate = "2099-01-01" }, Ct)));
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.PostAsJsonAsync("/api/v1/admin/developments",
            new { name = "x", kind = "metro", status = "proposed_unverified", authority = "x", sourceDate = "2026-01-10" }, Ct)).StatusCode);

        var road = await reviewer.PostAsJsonAsync("/api/v1/admin/developments",
            new { name = "NH-65 widening", kind = "road_widening", status = "approved", authority = "NHAI", sourceUrl = "https://nhai.gov.in/notice/123", sourceDate = "2026-01-10" }, Ct);
        Assert.Equal(HttpStatusCode.Created, road.StatusCode);
        var roadId = (await road.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var metro = await reviewer.PostAsJsonAsync("/api/v1/admin/developments",
            new { name = "Metro phase 2", kind = "metro", status = "proposed_unverified", authority = "HMRL", sourceDate = "2026-02-01" }, Ct);
        var metroId = (await metro.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.PutAsJsonAsync($"/api/v1/admin/parcels/{parcelId}/developments", new { projectId = metroId, distanceKm = 4.5m, mayAffectParcel = false }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.PutAsJsonAsync($"/api/v1/admin/parcels/{parcelId}/developments", new { projectId = roadId, distanceKm = 0.2m, mayAffectParcel = true }, Ct)).StatusCode);
        Assert.Equal("development.distance_invalid", await CodeOf(await reviewer.PutAsJsonAsync($"/api/v1/admin/parcels/{parcelId}/developments", new { projectId = roadId, distanceKm = -1m, mayAffectParcel = true }, Ct)));

        // Anyone sees them; the project that may touch the land comes first.
        var nearby = await fixture.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}/developments", Ct);
        Assert.Equal(2, nearby.GetArrayLength());
        Assert.Equal("NH-65 widening", nearby[0].GetProperty("name").GetString());
        Assert.True(nearby[0].GetProperty("mayAffectParcel").GetBoolean());
        Assert.Equal("https://nhai.gov.in/notice/123", nearby[0].GetProperty("sourceUrl").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.DeleteAsync($"/api/v1/admin/parcels/{parcelId}/developments/{metroId}", Ct)).StatusCode);
        Assert.Equal(1, (await fixture.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{parcelId}/developments", Ct)).GetArrayLength());

        // Reports: signed in, one open report per person and land, a reason; "other" needs details.
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.CreateClient().PostAsJsonAsync($"/api/v1/parcels/{parcelId}/reports", new { reason = "already_sold" }, Ct)).StatusCode);
        Assert.Equal("report.reason_invalid", await CodeOf(await buyer.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/reports", new { reason = "spam" }, Ct)));
        Assert.Equal("report.details_required", await CodeOf(await buyer.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/reports", new { reason = "other" }, Ct)));
        Assert.Equal(HttpStatusCode.Created, (await buyer.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/reports", new { reason = "already_sold", details = "Sold last month" }, Ct)).StatusCode);
        Assert.Equal("report.already_open", await CodeOf(await buyer.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/reports", new { reason = "already_sold" }, Ct)));

        var queue = await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/admin/reports", Ct);
        var report = queue.EnumerateArray().Single(r => r.GetProperty("parcelId").GetGuid() == parcelId);
        var reportId = report.GetProperty("id").GetGuid();
        Assert.Equal("report.note_required", await CodeOf(await reviewer.PostAsJsonAsync($"/api/v1/admin/reports/{reportId}/decision", new { decision = "resolve" }, Ct)));
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.PostAsJsonAsync($"/api/v1/admin/reports/{reportId}/decision", new { decision = "resolve", note = "Lister marked it sold" }, Ct)).StatusCode);
        Assert.Equal("report.already_decided", await CodeOf(await reviewer.PostAsJsonAsync($"/api/v1/admin/reports/{reportId}/decision", new { decision = "dismiss", note = "x" }, Ct)));

        // After it is handled, the same person may report again.
        Assert.Equal(HttpStatusCode.Created, (await buyer.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/reports", new { reason = "wrong_details" }, Ct)).StatusCode);
    }

    private async Task<HttpResponseMessage> Upload(HttpClient client, Guid parcelId, string type, byte[] bytes, bool declaration = true, string? reference = null)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(type), "type" },
            { new StringContent(declaration ? "true" : "false"), "declaration" },
            { new ByteArrayContent(bytes), "file", "upload.bin" },
        };
        if (reference is not null)
        {
            form.Add(new StringContent(reference), "referenceNumber");
        }

        return await client.PostAsync($"/api/v1/parcels/{parcelId}/documents", form, Ct);
    }

    private async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();

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
