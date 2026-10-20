using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Reporting;
using Shouldly;
using Xunit;
using static Gigtime.Domain.Tests.Reporting.ReportScenario;

namespace Gigtime.Domain.Tests.Reporting;

public sealed class ReportPeriodTests
{
	[Fact]
	public void Configured_week_start_and_dst_use_local_dates_for_both_calculations()
	{
		var s = new ReportScenario("America/New_York");
		s.Catalogue.Send(new SetDefaults("UTC", DayOfWeek.Sunday));
		var query = new ReportQuery(At("2026-03-08T12:00:00Z"));
		var period = ReportPeriod.For(query, s.Catalogue.State.Resolve(s.Catalogue.ProjectId));
		period.From.ShouldBe(new(2026, 3, 8));
		period.UntilExclusive.ShouldBe(new(2026, 3, 15));
		period.Contains(At("2026-03-15T03:59:59Z"), "America/New_York").ShouldBeTrue();
		period.Contains(At("2026-03-15T04:00:00Z"), "America/New_York").ShouldBeFalse();
	}
}
