using MediatR;
using Microsoft.EntityFrameworkCore;
using WolfDen.Application.Requests.DTOs.Attendence;
using WolfDen.Domain.Entity;
using WolfDen.Domain.Enums;
using WolfDen.Infrastructure.Data;

namespace WolfDen.Application.Requests.Queries.Attendence.DailyStatus
{
    public class DailyDetailsQueryHandler :IRequestHandler<DailyDetailsQuery,DailyAttendanceDTO>
    {
        private readonly WolfDenContext _context;
        public DailyDetailsQueryHandler(WolfDenContext context)
        {
            _context = context;
        }
        public async Task<DailyAttendanceDTO> Handle(DailyDetailsQuery request, CancellationToken cancellationToken)
        {
            DateOnly currentDate=request.Date;
            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
            if(currentDate==today)
            {
                DailyAttendanceDTO holiday = new DailyAttendanceDTO();
                holiday.AttendanceStatusId = AttendanceStatus.OngoingShift;
                return holiday;
            }
            if (currentDate.DayOfWeek == DayOfWeek.Saturday || currentDate.DayOfWeek == DayOfWeek.Sunday)
            {
                DailyAttendanceDTO holiday=new DailyAttendanceDTO();
                holiday.AttendanceStatusId=AttendanceStatus.NormalHoliday;
                return holiday;    
            }
            int minWorkDuration = 360;
            DailyAttendanceDTO? attendence = await _context.DailyAttendence
                .Where(x => x.EmployeeId == request.EmployeeId && x.Date == request.Date)
                .Select(x => new DailyAttendanceDTO
            {
                ArrivalTime = x.ArrivalTime,
                DepartureTime = x.DepartureTime,
                InsideHours = x.InsideDuration,
                OutsideHours = x.OutsideDuration,
                MissedPunch = x.MissedPunch,
            }).FirstOrDefaultAsync(cancellationToken);

            if (attendence is null)
            {
                DailyAttendanceDTO notPresentDay = new DailyAttendanceDTO();
                Holiday? holiday = await _context.Holiday.Where(x => x.Date == request.Date)
                    .FirstOrDefaultAsync(cancellationToken);
                if (holiday is not null)
                {
                    if (holiday.Type is AttendanceStatus.NormalHoliday)
                    {
                        notPresentDay.AttendanceStatusId = AttendanceStatus.NormalHoliday;
                    }
                    else
                    {
                        LeaveRequest? leave = await _context.LeaveRequests
                            .Where(x => x.EmployeeId == request.EmployeeId && x.FromDate <= request.Date && request.Date <= x.ToDate && x.LeaveRequestStatusId == LeaveRequestStatus.Approved)
                            .Include(x => x.LeaveType)
                            .FirstOrDefaultAsync(cancellationToken);

                        notPresentDay.AttendanceStatusId = (leave is null) ? AttendanceStatus.Absent : AttendanceStatus.RestrictedHoliday;
                    }
                }
                else
                {
                    LeaveRequest? leave = await _context.LeaveRequests
                        .Where(x => x.EmployeeId == request.EmployeeId && x.FromDate <= request.Date && request.Date <= x.ToDate && x.LeaveRequestStatusId == LeaveRequestStatus.Approved)
                        .Include(x => x.LeaveType)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (leave is null)
                    {
                        notPresentDay.AttendanceStatusId = AttendanceStatus.Absent;
                    }
                    else
                    {
                        notPresentDay.AttendanceStatusId = (leave.LeaveType.LeaveCategoryId is LeaveCategory.WorkFromHome)
                            ? AttendanceStatus.WFH : AttendanceStatus.Leave;
                    }
                }
                return notPresentDay;
            }
            else
            {
                attendence.AttendanceStatusId = (attendence.InsideHours >= minWorkDuration) ?
                    AttendanceStatus.Present : AttendanceStatus.IncompleteShift;
            }
            List<AttendenceLogDTO> attendenceRecords = await _context.AttendenceLog
                .Where(x => x.EmployeeId == request.EmployeeId && x.PunchDate == request.Date)
                .Include(x => x.Device)
                .Select(x => new AttendenceLogDTO
                {
                    Time = x.PunchTime,
                    DeviceName = x.Device.Name,
                    Direction = x.Direction
                }).ToListAsync(cancellationToken);
             if(attendence is not null)
            attendence.DailyLog = attendenceRecords;
            return attendence;
        }
    }
}