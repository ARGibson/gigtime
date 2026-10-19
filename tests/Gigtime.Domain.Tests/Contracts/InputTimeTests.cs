using System.Globalization;
using Gigtime.Contracts;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Contracts;

public sealed class InputTimeTests
{
	private static readonly DateTimeOffset now = Parse("2026-03-03T02:00:00Z");

	[Theory]
	[InlineData("America/New_York", "2026-03-08T02:30")]
	[InlineData("Europe/London", "2026-03-29T01:30")]
	[InlineData("Pacific/Apia", "2011-12-30T12:00")]
	public void Skipped_local_times_are_refused(string timezone, string text)
	{
		InputTime.IsValid(text).ShouldBeTrue();
		InputTime.TryResolve(text, timezone, now, out _).ShouldBeFalse();
	}

	[Theory]
	[InlineData("America/New_York", "2026-11-01T01:30")]
	[InlineData("Europe/London", "2026-10-25T01:30")]
	[InlineData("America/St_Johns", "2010-11-06T23:30")]
	public void Repeated_local_times_are_refused(string timezone, string text)
	{
		InputTime.IsValid(text).ShouldBeTrue();
		InputTime.TryResolve(text, timezone, now, out _).ShouldBeFalse();
	}

	[Theory]
	[InlineData("America/New_York")]
	[InlineData("america/new_york")]
	[InlineData("US/Eastern")]
	[InlineData("Eastern Standard Time")]
	public void Clock_times_use_today_in_the_clients_zone_for_compatible_ids(string timezone)
	{
		InputTime.TryResolve("23:30", timezone, now, out var resolved).ShouldBeTrue();
		resolved.ShouldBe(Parse("2026-03-03T04:30:00Z"));
		resolved.Offset.ShouldBe(TimeSpan.Zero);
	}

	[Theory]
	[InlineData("Etc/UTC")]
	[InlineData("UTC")]
	public void UTC_aliases_preserve_local_instants(string timezone)
	{
		InputTime.TryResolve("2026-03-02T09:00:00.1234567", timezone, now, out var resolved).ShouldBeTrue();
		resolved.ShouldBe(Parse("2026-03-02T09:00:00.1234567Z"));
	}

	[Fact]
	public void Explicit_offsets_disambiguate_repeated_times_without_resolving_the_clients_zone()
	{
		InputTime.TryResolve("2026-11-01T01:30:00-04:00", "not/a-zone", now, out var resolved).ShouldBeTrue();
		resolved.ShouldBe(Parse("2026-11-01T05:30:00Z"));
		resolved.Offset.ShouldBe(TimeSpan.FromHours(-4));
	}

	[Fact]
	public void Local_times_in_an_unsupported_zone_raise_a_clear_repair_message()
	{
		var exception = Should.Throw<TimeZoneNotFoundException>(() => InputTime.TryResolve("09:00", "not/a-zone", now, out _));
		exception.Message.ShouldContain("not/a-zone");
		exception.Message.ShouldContain("Set the client or defaults timezone");
	}

	private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
