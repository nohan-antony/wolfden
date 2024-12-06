﻿using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WolfDen.Application.DTOs.Attendence;
using WolfDen.Domain.ConfigurationModel;
using WolfDen.Domain.Entity;
using WolfDen.Domain.Enums;
using WolfDen.Infrastructure.Data;

namespace WolfDen.Application.Requests.Queries.Attendence.AttendanceSummary
{
    public class AttendanceSummaryQueryHandler(WolfDenContext context, IOptions<OfficeDurationSettings> officeDurationSettings) : IRequestHandler<AttendanceSummaryQuery, AttendanceSummaryDTO>
    {
        private readonly WolfDenContext _context = context;
        private readonly IOptions<OfficeDurationSettings> _officeDurationSettings=officeDurationSettings;
        public async Task<AttendanceSummaryDTO> Handle(AttendanceSummaryQuery request, CancellationToken cancellationToken)
        {
            int minWorkDuration = _officeDurationSettings.Value.MinWorkDuration;

            DateOnly monthStart = new DateOnly(request.Year, request.Month, 1);
            DateOnly monthEnd = monthStart.AddMonths(1).AddDays(-1);

            AttendanceSummaryDTO summaryDto = new AttendanceSummaryDTO
            {
                Present = 0,
                Absent = 0,
                IncompleteShift = 0,
                RestrictedHoliday = 0,
                NormalHoliday = 0,
                WFH = 0,
                Leave = 0,
                HalfDay=0
            };

            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

            List<DailyAttendence> attendanceRecords = await _context.DailyAttendence
                .Where(x => x.EmployeeId == request.EmployeeId && x.Date >= monthStart && x.Date <= monthEnd)
                .ToListAsync(cancellationToken);

            List<Holiday> holidays = await _context.Holiday
                .Where(x => x.Date >= monthStart && x.Date <= monthEnd)
                .ToListAsync(cancellationToken);

            List<LeaveRequest> leaveRequests = await _context.LeaveRequests
                .Where(x => x.EmployeeId == request.EmployeeId &&
                            x.FromDate <= monthEnd && x.ToDate >= monthStart &&
                            x.LeaveRequestStatusId == LeaveRequestStatus.Approved)
                .ToListAsync(cancellationToken);

            List<LeaveType> leaveTypes = await _context.LeaveTypes.ToListAsync(cancellationToken);

            List<LeaveRequest> leave = leaveRequests
                .Where(x => x.HalfDay == true)
                .ToList();

            Dictionary<DateOnly, LeaveRequest> leaveDictionary = leave.ToDictionary(x => x.FromDate);

            for (DateOnly currentDate = monthStart; currentDate <= monthEnd; currentDate = currentDate.AddDays(1))
            {
                if (currentDate > today) 
                {
                    break;
                }

                if (currentDate.DayOfWeek == DayOfWeek.Saturday || currentDate.DayOfWeek == DayOfWeek.Sunday)
                {
                    continue; 
                }

                LeaveRequest? halfDay = leaveDictionary.GetValueOrDefault(currentDate);

                if (halfDay is not null)
                {
                    minWorkDuration = minWorkDuration / 2;
                }

                DailyAttendence? attendanceRecord = attendanceRecords.FirstOrDefault(x => x.Date == currentDate);
                if (attendanceRecord is not null)
                {

                    if (attendanceRecord.InsideDuration >= minWorkDuration)
                    {
                        if (halfDay is not null)
                        {
                            summaryDto.HalfDay++;
                        }
                        else
                        summaryDto.Present++;
                    }
                    else
                    {
                        summaryDto.IncompleteShift++;
                    }
                }
                else
                {
                    Holiday holiday = holidays.FirstOrDefault(x => x.Date == currentDate);
                    if (holiday is not null)
                    {

                        if (holiday.Type is AttendanceStatus.NormalHoliday)
                        {
                            summaryDto.NormalHoliday++;
                        }

                        else if (holiday.Type is AttendanceStatus.RestrictedHoliday)
                        {
                            LeaveRequest leaveRequestForHoliday = leaveRequests.FirstOrDefault(x => x.FromDate <= currentDate && x.ToDate >= currentDate);

                            if (leaveRequestForHoliday is not null)
                            {
                                var leaveType = leaveTypes.FirstOrDefault(x => x.Id == leaveRequestForHoliday.TypeId);

                                if (leaveType is not null && leaveType.LeaveCategoryId is LeaveCategory.RestrictedHoliday)
                                {
                                    summaryDto.RestrictedHoliday++;
                                }
                            }
                        }
                    }

                    else
                    {
                        LeaveRequest leaveRequest = leaveRequests.FirstOrDefault(x => x.FromDate <= currentDate && x.ToDate >= currentDate);
                        if (leaveRequest is not null)
                        {
                            var leaveType = leaveTypes.FirstOrDefault(x => x.Id == leaveRequest.TypeId);
                            if (leaveType is not null && leaveType.LeaveCategoryId is LeaveCategory.WorkFromHome)
                            {
                                summaryDto.WFH++;
                            }
                            else
                            {
                                summaryDto.Leave++;
                            }
                        }
                        else
                        {
                            summaryDto.Absent++;
                        }
                    }
                }
            }
            return summaryDto;
        }
    }
}
