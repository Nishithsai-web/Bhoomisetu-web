using Bhoomisetu.Modules.Identity;
using Xunit;

namespace Bhoomisetu.UnitTests.Identity;

public sealed class OtpServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
    private readonly FakeClock _clock = new(Start);
    private readonly InMemoryStore _store = new();
    private readonly CapturingSender _sender = new();
    private readonly IndianMobileNumber _phone = IndianMobileNumber.Parse("9876543210").Value;
    private readonly OtpService _service;

    public OtpServiceTests()
    {
        _service = new OtpService(_store, _sender, _clock, new OtpOptions { Pepper = new byte[32] });
    }

    [Fact]
    public async Task Sends_a_six_digit_code_and_stores_only_a_hash()
    {
        var outcome = await _service.RequestAsync(_phone, CancellationToken.None);

        Assert.Equal(OtpRequestStatus.Sent, outcome.Status);
        Assert.Matches("^[0-9]{6}$", _sender.LastCode);
        var stored = Assert.Single(_store.Challenges);
        Assert.Equal(32, stored.CodeHash.Length);
        Assert.DoesNotContain(_sender.LastCode!, Convert.ToHexString(stored.CodeHash), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Correct_code_verifies_once_only()
    {
        await _service.RequestAsync(_phone, CancellationToken.None);
        var code = _sender.LastCode;

        Assert.Equal(OtpVerifyStatus.Verified, await _service.VerifyAsync(_phone, code, CancellationToken.None));
        Assert.Equal(OtpVerifyStatus.NoActiveChallenge, await _service.VerifyAsync(_phone, code, CancellationToken.None));
    }

    [Fact]
    public async Task Code_typed_in_telugu_digits_verifies()
    {
        await _service.RequestAsync(_phone, CancellationToken.None);
        var telugu = new string(_sender.LastCode!.Select(c => (char)('౦' + (c - '0'))).ToArray());
        Assert.Equal(OtpVerifyStatus.Verified, await _service.VerifyAsync(_phone, telugu, CancellationToken.None));
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        await _service.RequestAsync(_phone, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(OtpVerifyStatus.Expired, await _service.VerifyAsync(_phone, _sender.LastCode, CancellationToken.None));
    }

    [Fact]
    public async Task Locks_after_five_wrong_codes_even_if_the_right_one_follows()
    {
        await _service.RequestAsync(_phone, CancellationToken.None);
        var wrong = _sender.LastCode == "000000" ? "111111" : "000000";

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(OtpVerifyStatus.Invalid, await _service.VerifyAsync(_phone, wrong, CancellationToken.None));
        }

        Assert.Equal(OtpVerifyStatus.TooManyAttempts, await _service.VerifyAsync(_phone, wrong, CancellationToken.None));
        Assert.Equal(OtpVerifyStatus.TooManyAttempts, await _service.VerifyAsync(_phone, _sender.LastCode, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abcdef")]
    [InlineData("1234567")]
    public async Task Malformed_codes_are_invalid(string? code)
    {
        await _service.RequestAsync(_phone, CancellationToken.None);
        Assert.Equal(OtpVerifyStatus.Invalid, await _service.VerifyAsync(_phone, code, CancellationToken.None));
    }

    [Fact]
    public async Task Resend_within_cooldown_is_refused_with_wait_time()
    {
        await _service.RequestAsync(_phone, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(20));

        var outcome = await _service.RequestAsync(_phone, CancellationToken.None);
        Assert.Equal(OtpRequestStatus.TooSoon, outcome.Status);
        Assert.Equal(TimeSpan.FromSeconds(40), outcome.RetryAfter);
    }

    [Fact]
    public async Task Sixth_request_in_an_hour_is_rate_limited()
    {
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(OtpRequestStatus.Sent, (await _service.RequestAsync(_phone, CancellationToken.None)).Status);
            _clock.Advance(TimeSpan.FromMinutes(2));
        }

        Assert.Equal(OtpRequestStatus.RateLimited, (await _service.RequestAsync(_phone, CancellationToken.None)).Status);
        _clock.Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(OtpRequestStatus.Sent, (await _service.RequestAsync(_phone, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_new_code_replaces_the_old_one()
    {
        await _service.RequestAsync(_phone, CancellationToken.None);
        var first = _sender.LastCode;
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _service.RequestAsync(_phone, CancellationToken.None);

        if (first != _sender.LastCode)
        {
            Assert.Equal(OtpVerifyStatus.Invalid, await _service.VerifyAsync(_phone, first, CancellationToken.None));
        }

        Assert.Equal(OtpVerifyStatus.Verified, await _service.VerifyAsync(_phone, _sender.LastCode, CancellationToken.None));
    }

    [Fact]
    public async Task Daily_cap_applies_across_hours()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(OtpRequestStatus.Sent, (await _service.RequestAsync(_phone, CancellationToken.None)).Status);
            _clock.Advance(TimeSpan.FromMinutes(13)); // 5 per hour stays under the hourly cap
        }

        _clock.Advance(TimeSpan.FromHours(1));
        var outcome = await _service.RequestAsync(_phone, CancellationToken.None);
        Assert.Equal(OtpRequestStatus.RateLimited, outcome.Status);
        Assert.Equal(TimeSpan.FromDays(1), outcome.RetryAfter);
    }

    [Fact]
    public async Task Global_sms_budget_stops_sending()
    {
        var service = new OtpService(_store, _sender, _clock, new OtpOptions { Pepper = new byte[32], MaxSmsPerHourGlobal = 2 });
        Assert.Equal(OtpRequestStatus.Sent, (await service.RequestAsync(IndianMobileNumber.Parse("9000000001").Value, CancellationToken.None)).Status);
        Assert.Equal(OtpRequestStatus.Sent, (await service.RequestAsync(IndianMobileNumber.Parse("9000000002").Value, CancellationToken.None)).Status);
        Assert.Equal(OtpRequestStatus.Busy, (await service.RequestAsync(IndianMobileNumber.Parse("9000000003").Value, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Parallel_requests_for_one_number_send_only_one_code()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _service.RequestAsync(_phone, CancellationToken.None)));
        Assert.Equal(1, results.Count(r => r.Status == OtpRequestStatus.Sent));
        Assert.Single(_store.Challenges);
    }

    [Fact]
    public void Short_pepper_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => new OtpService(_store, _sender, _clock, new OtpOptions { Pepper = new byte[16] }));
    }

    private sealed class CapturingSender : IOtpSender
    {
        public string? LastCode { get; private set; }

        public Task SendAsync(IndianMobileNumber phone, string code, TimeSpan validFor, CancellationToken cancellationToken)
        {
            LastCode = code;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryStore : IOtpChallengeStore
    {
        private readonly object _gate = new();
        private readonly SemaphoreSlim _phoneLock = new(1, 1);

        public List<OtpChallenge> Challenges { get; } = [];

        public Task<OtpChallenge?> GetLatestAsync(string phoneE164, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(Challenges.Where(c => c.PhoneE164 == phoneE164).OrderByDescending(c => c.CreatedAt).FirstOrDefault());
            }
        }

        public Task<int> CountCreatedSinceAsync(string phoneE164, DateTimeOffset since, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(Challenges.Count(c => c.PhoneE164 == phoneE164 && c.CreatedAt >= since));
            }
        }

        public Task<int> CountAllCreatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(Challenges.Count(c => c.CreatedAt >= since));
            }
        }

        public async Task<IOtpPhoneLock> LockPhoneAsync(string phoneE164, CancellationToken cancellationToken)
        {
            await _phoneLock.WaitAsync(cancellationToken);
            return new Releaser(_phoneLock);
        }

        public Task AddAsync(OtpChallenge challenge, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Challenges.Add(challenge);
            }

            return Task.CompletedTask;
        }

        public Task<bool> TryRecordAttemptAsync(Guid challengeId, int maxAttempts, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var index = Challenges.FindIndex(c => c.Id == challengeId);
                var current = Challenges[index];
                if (current.ConsumedAt is not null || current.Attempts >= maxAttempts)
                {
                    return Task.FromResult(false);
                }

                Challenges[index] = Copy(current, current.Attempts + 1, current.ConsumedAt);
                return Task.FromResult(true);
            }
        }

        public Task<bool> TryConsumeAsync(Guid challengeId, DateTimeOffset consumedAt, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var index = Challenges.FindIndex(c => c.Id == challengeId);
                var current = Challenges[index];
                if (current.ConsumedAt is not null)
                {
                    return Task.FromResult(false);
                }

                Challenges[index] = Copy(current, current.Attempts, consumedAt);
                return Task.FromResult(true);
            }
        }

        private sealed class Releaser(SemaphoreSlim semaphore) : IOtpPhoneLock
        {
            public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public ValueTask DisposeAsync()
            {
                semaphore.Release();
                return ValueTask.CompletedTask;
            }
        }

        private static OtpChallenge Copy(OtpChallenge c, int attempts, DateTimeOffset? consumedAt) => new()
        {
            Id = c.Id,
            PhoneE164 = c.PhoneE164,
            CodeHash = c.CodeHash,
            CreatedAt = c.CreatedAt,
            ExpiresAt = c.ExpiresAt,
            Attempts = attempts,
            ConsumedAt = consumedAt,
        };
    }
}
