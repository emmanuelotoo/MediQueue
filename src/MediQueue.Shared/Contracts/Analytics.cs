namespace MediQueue.Shared.Contracts;

/// <summary>Headline numbers for a date range, across all departments.</summary>
public class AnalyticsSummaryDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }

    public int TotalCheckIns { get; set; }
    public int Completed { get; set; }
    public int NoShows { get; set; }
    public int Cancelled { get; set; }
    public int StillWaiting { get; set; }

    public int? AverageWaitMinutes { get; set; }
    public int? LongestWaitMinutes { get; set; }
    public int? AverageConsultationMinutes { get; set; }

    /// <summary>Share of patients who were called but never appeared, 0-100.</summary>
    public double NoShowRate { get; set; }

    public List<DepartmentPerformanceDto> Departments { get; set; } = [];
    public List<DailyVolumeDto> DailyVolume { get; set; } = [];
    public List<HourlyVolumeDto> HourlyVolume { get; set; } = [];
}

public class DepartmentPerformanceDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string DepartmentCode { get; set; } = string.Empty;
    public int CheckIns { get; set; }
    public int Completed { get; set; }
    public int NoShows { get; set; }
    public int? AverageWaitMinutes { get; set; }
    public int? AverageConsultationMinutes { get; set; }

    /// <summary>Patients completed per hour the department was open.</summary>
    public double ThroughputPerHour { get; set; }
}

public class DailyVolumeDto
{
    public DateOnly Date { get; set; }
    public int CheckIns { get; set; }
    public int Completed { get; set; }
    public int? AverageWaitMinutes { get; set; }
}

/// <summary>Check-ins by hour of day, for spotting the morning crush.</summary>
public class HourlyVolumeDto
{
    public int Hour { get; set; }
    public int CheckIns { get; set; }
}
