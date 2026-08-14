namespace MediQueue.Client.Tests;

public class TicketCodeTests : BunitContext
{
    [Fact]
    public void The_department_prefix_is_set_apart_from_the_number()
    {
        var component = Render<TicketCode>(p => p.Add(c => c.Code, "CAR-014"));

        Assert.Equal("CAR-", component.Find(".code__prefix").TextContent);
        Assert.Equal("CAR-014", component.Find(".code").TextContent);
    }

    [Fact]
    public void A_code_without_a_prefix_still_renders()
    {
        var component = Render<TicketCode>(p => p.Add(c => c.Code, "014"));

        Assert.Empty(component.FindAll(".code__prefix"));
        Assert.Equal("014", component.Find(".code").TextContent);
    }
}

public class WaitTimeTests : BunitContext
{
    [Theory]
    [InlineData(0, "Next")]
    [InlineData(25, "25 min")]
    [InlineData(59, "59 min")]
    [InlineData(75, "about 1 hr 15 min")]
    [InlineData(150, "about 2 hr 30 min")]
    public void Durations_are_worded_the_way_a_person_would_say_them(int minutes, string expected)
    {
        var component = Render<WaitTime>(p => p.Add(c => c.Minutes, minutes));

        Assert.Equal(expected, component.Find("span").TextContent);
    }

    [Fact]
    public void An_unknown_wait_shows_a_dash_rather_than_a_guess()
    {
        var component = Render<WaitTime>(p => p.Add(c => c.Minutes, (int?)null));

        Assert.Equal("—", component.Find("span").TextContent);
    }

    [Fact]
    public void The_zero_case_is_worded_by_the_screen_showing_it()
    {
        var component = Render<WaitTime>(p => p
            .Add(c => c.Minutes, 0)
            .Add(c => c.ZeroText, "No wait"));

        Assert.Equal("No wait", component.Find("span").TextContent);
    }
}

public class StatusBadgeTests : BunitContext
{
    [Theory]
    [InlineData(TicketStatus.Waiting, "Waiting")]
    [InlineData(TicketStatus.Called, "Called")]
    [InlineData(TicketStatus.InConsultation, "In room")]
    [InlineData(TicketStatus.Completed, "Seen")]
    [InlineData(TicketStatus.NoShow, "No show")]
    [InlineData(TicketStatus.Cancelled, "Left")]
    public void Statuses_are_named_in_words_staff_use(TicketStatus status, string expected)
    {
        var component = Render<StatusBadge>(p => p.Add(c => c.Status, status));

        Assert.Equal(expected, component.Find(".badge").TextContent.Trim());
    }
}

public class HourlyChartTests : BunitContext
{
    [Fact]
    public void The_busiest_hour_is_marked_and_named()
    {
        var hours = new List<HourlyVolumeDto>
        {
            new() { Hour = 7, CheckIns = 4 },
            new() { Hour = 8, CheckIns = 19 },
            new() { Hour = 9, CheckIns = 11 }
        };

        var component = Render<HourlyChart>(p => p.Add(c => c.Hours, hours));

        Assert.Single(component.FindAll(".hours__bar--peak"));
        Assert.Contains("Busiest at 8:00", component.Markup);
    }

    [Fact]
    public void An_hour_with_no_arrivals_still_draws_a_measured_zero()
    {
        var hours = new List<HourlyVolumeDto>
        {
            new() { Hour = 7, CheckIns = 0 },
            new() { Hour = 8, CheckIns = 10 }
        };

        var component = Render<HourlyChart>(p => p.Add(c => c.Hours, hours));

        // A bar exists for the empty hour, so the gap reads as data, not a fault.
        Assert.Equal(2, component.FindAll(".hours__bar").Count);
    }
}

public class DepartmentBarsTests : BunitContext
{
    [Fact]
    public void Departments_are_ordered_by_the_measure_being_compared()
    {
        var rows = new List<DepartmentBars.Row>
        {
            new("Dental", 3, "3"),
            new("Cardiology", 12, "12"),
            new("Paediatrics", 7, "7")
        };

        var component = Render<DepartmentBars>(p => p
            .Add(c => c.Rows, rows)
            .Add(c => c.Title, "Patients seen"));

        var names = component.FindAll(".bars__name").Select(n => n.TextContent).ToList();
        Assert.Equal(["Cardiology", "Paediatrics", "Dental"], names);
    }

    [Fact]
    public void An_empty_range_says_so()
    {
        var component = Render<DepartmentBars>(p => p
            .Add(c => c.Rows, new List<DepartmentBars.Row>())
            .Add(c => c.Title, "Patients seen"));

        Assert.Contains("No activity", component.Markup);
    }
}
