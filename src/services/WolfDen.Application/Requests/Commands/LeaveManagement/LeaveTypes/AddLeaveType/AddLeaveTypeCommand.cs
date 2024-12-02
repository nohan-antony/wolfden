﻿using MediatR;
using WolfDen.Domain.Enums;

namespace WolfDen.Application.Requests.Commands.LeaveManagement.LeaveTypes.AddLeaveType
{
    public class AddLeaveTypeCommand : IRequest<bool>
    {
        public string TypeName { get; set; }
        public int? MaxDays { get; set; }
        public bool? IsHalfDayAllowed { get; set; }
        public int? IncrementCount { get; set; }
        public LeaveIncrementGapMonth? IncrementGapId { get; set; }
        public bool? CarryForward { get; set; }
        public int? CarryForwardLimit { get; set; }
        public int? DaysCheck { get; set; }
        public int? DaysCheckMore { get; set; }
        public int? DaysCheckEqualOrLess { get; set; }
        public int? DutyDaysRequired { get; set; }
        public bool? Sandwich { get; set; }

    }
}
