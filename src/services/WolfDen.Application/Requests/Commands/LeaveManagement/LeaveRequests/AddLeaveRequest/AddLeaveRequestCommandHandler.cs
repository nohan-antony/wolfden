﻿using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using sib_api_v3_sdk.Model;
using WolfDen.Application.DTOs.LeaveManagement;
using WolfDen.Application.Helper.LeaveManagement;
using WolfDen.Application.Helpers;
using WolfDen.Application.Method.LeaveManagement;
using WolfDen.Application.Requests.Commands.Attendence.SendNotification;
using WolfDen.Application.Requests.Commands.LeaveManagement.LeaveRequestDays;
using WolfDen.Domain.Entity;
using WolfDen.Domain.Enums;
using WolfDen.Infrastructure.Data;
using static WolfDen.Domain.Enums.EmployeeEnum;

namespace WolfDen.Application.Requests.Commands.LeaveManagement.LeaveRequests.AddLeaveRequest
{
    public class AddLeaveRequestCommandHandler(WolfDenContext context, AddLeaveRequestValidator validator, IMediator mediator,IConfiguration configuration, ManagerEmailFinder emailFinder, Email email, UserManager<User> userManager) : IRequestHandler<AddLeaveRequestCommand, bool>
    {
        private readonly WolfDenContext _context = context;
        private readonly AddLeaveRequestValidator _validator = validator;
        private readonly IMediator _mediator = mediator;
        private readonly string _apiKey = configuration["BrevoApi:ApiKey"];
        private readonly string _senderEmail = configuration["BrevoApi:SenderEmail"];
        private readonly string _senderName = configuration["BrevoApi:SenderName"]; 
        private readonly ManagerEmailFinder _emailFinder = emailFinder;
        private readonly Email _email = email;
        private readonly UserManager<User> _userManager = userManager;

        public async Task<bool> Handle(AddLeaveRequestCommand request, CancellationToken cancellationToken)
        {

            var result = await _validator.ValidateAsync(request, cancellationToken);
            if (!result.IsValid)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.ErrorMessage));
                throw new ValidationException($"Validation failed: {errors}");
            }
            LeaveBalance leaveBalance = await _context.LeaveBalances.FirstOrDefaultAsync(x => x.EmployeeId == request.EmpId && x.TypeId == request.TypeId, cancellationToken);
            Employee employee = await _context.Employees.FirstOrDefaultAsync(x => x.Id == request.EmpId, cancellationToken);
            LeaveType leaveType = await _context.LeaveTypes.FirstOrDefaultAsync(x => x.Id == request.TypeId);

            User user = await _userManager.FindByIdAsync(employee.UserId);
            var currentRoles = await _userManager.GetRolesAsync(user);
            string rolesString = string.Join(",", currentRoles);

            if (leaveType is null)
            {
                throw new InvalidOperationException($"No Such Leave Type");
            }
            if (employee is null)
            {
                throw new InvalidOperationException($"No Such Employee");
            }

            if (leaveBalance is null)
            {
                throw new InvalidOperationException($"Leave balance not found for Employee ID {employee.FirstName} and Type ID {leaveType.TypeName}.");
            }
            decimal virtualLeaveCountWithoutHalfDay = await _context.LeaveRequestDays.Where(x => x.LeaveRequest.EmployeeId == request.EmpId && x.LeaveRequest.TypeId == request.TypeId && x.LeaveRequest.LeaveRequestStatusId == LeaveRequestStatus.Open && x.LeaveRequest.HalfDay !=true).CountAsync(cancellationToken);
            decimal virtualLeaveCountWithHalfDay = await _context.LeaveRequestDays.Where(x => x.LeaveRequest.EmployeeId == request.EmpId && x.LeaveRequest.TypeId == request.TypeId && x.LeaveRequest.LeaveRequestStatusId == LeaveRequestStatus.Open && x.LeaveRequest.HalfDay == true).CountAsync(cancellationToken);
            decimal virtualBalance = leaveBalance.Balance - (virtualLeaveCountWithoutHalfDay + (virtualLeaveCountWithHalfDay/2));
            decimal days = 0;
            List<DateOnly> dates = new List<DateOnly>();
            DateOnly currentDate = DateOnly.FromDateTime(DateTime.Now);
            CalculateLeaveDays calculateLeaveDays = new CalculateLeaveDays(_context);
            bool sandwich = leaveType.Sandwich ?? false;
            LeaveDaysResultDto leaveDaysResultDto = await calculateLeaveDays.LeaveDays(request.FromDate, request.ToDate, sandwich);
            days = request.HalfDay == true ? (leaveDaysResultDto.DaysCount / 2) : leaveDaysResultDto.DaysCount;
            dates = leaveDaysResultDto.ValidDate;
            bool dateCheck = await _context.LeaveRequestDays
                .Where(x => dates.Contains(x.LeaveDate) && x.LeaveRequest.TypeId == request.TypeId && x.LeaveRequest.EmployeeId == request.EmpId &&
                            (x.LeaveRequest.LeaveRequestStatusId == LeaveRequestStatus.Open ||
                                x.LeaveRequest.LeaveRequestStatusId == LeaveRequestStatus.Approved))
                .AnyAsync(cancellationToken);
            if (!dateCheck) 
            {
                if (employee.Gender.HasValue && ((employee.Gender == Gender.Male && leaveType.LeaveCategoryId != LeaveCategory.Maternity) || (employee.Gender == Gender.Female && leaveType.LeaveCategoryId != LeaveCategory.Paternity)))
                {
                    if (leaveType.LeaveCategoryId == LeaveCategory.WorkFromHome && currentDate < request.FromDate)
                    {
                        if (leaveType.DaysCheck.HasValue && (days > leaveType.DaysCheck))
                        {
                            if (request.FromDate.DayNumber - currentDate.DayNumber >= leaveType.DaysCheckMore)
                            {
                                return await WorkFromHomeCheck();

                            }
                            else
                            {
                                throw new InvalidOperationException($"For more than {leaveType.DaysCheck} {leaveType.TypeName}, leave FromDate should be atleast {leaveType.DaysCheckMore} days before. ");
                            }
                        }
                        else if (leaveType.DaysCheck.HasValue && (days <= leaveType.DaysCheck))
                        {
                            if (request.FromDate.DayNumber - currentDate.DayNumber >= leaveType.DaysCheckEqualOrLess)
                            {
                                return await WorkFromHomeCheck();
                            }
                            else
                            {
                                throw new InvalidOperationException($"For less than or {leaveType.DaysCheck} {leaveType.TypeName}, leave should be applied atleast {leaveType.DaysCheckEqualOrLess} days before. ");
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException($"Days Check Not Assinged");
                        }
                    }


                    else if (currentDate.DayNumber < request.FromDate.DayNumber && leaveType.LeaveCategoryId != LeaveCategory.BereavementLeave)
                    {
                        if (leaveBalance.Balance >= days && virtualBalance >= days)
                        {
                            if (!request.HalfDay.HasValue || request.HalfDay == false)
                            {
                                if (leaveType.DaysCheck.HasValue)
                                {
                                    if (days > leaveType.DaysCheck)
                                    {
                                        if (request.FromDate.DayNumber - currentDate.DayNumber >= leaveType.DaysCheckMore)
                                        {
                                            return await CheckOne();
                                        }
                                        else
                                        {
                                            throw new InvalidOperationException($"For more than {leaveType.DaysCheck} {leaveType.TypeName}, leave FromDate should be atleast {leaveType.DaysCheckMore} days before. ");
                                        }
                                    }
                                    else
                                    {
                                        if (request.FromDate.DayNumber - currentDate.DayNumber >= leaveType.DaysCheckEqualOrLess)
                                        {
                                            return await CheckOne();
                                        }
                                        else
                                        {
                                            throw new InvalidOperationException($"For less than or {leaveType.DaysCheck} {leaveType.TypeName}, leave should be applied atleast {leaveType.DaysCheckEqualOrLess} days before. ");
                                        }
                                    }
                                }
                                else
                                {
                                    throw new InvalidOperationException("Days Check Not Assinged");
                                }


                            }
                            else
                            {
                                if (leaveType.IsHalfDayAllowed == true)
                                {
                                    if (leaveDaysResultDto.DaysCount == 1)
                                    {
                                        if (leaveType.DaysCheck.HasValue)
                                        {
                                            if (days > leaveType.DaysCheck)
                                            {
                                                if (request.FromDate.DayNumber - currentDate.DayNumber >= leaveType.DaysCheckMore)
                                                {
                                                    return await CheckOne();

                                                }
                                                else
                                                {
                                                    throw new InvalidOperationException($"For more than {leaveType.DaysCheck} {leaveType.TypeName}, leave FromDate should be atleast {leaveType.DaysCheckMore} days before Apply Date. ");
                                                }
                                            }
                                            else
                                            {
                                                if (request.FromDate.DayNumber - currentDate.DayNumber >= leaveType.DaysCheckEqualOrLess)
                                                {
                                                    return await CheckOne();
                                                }
                                                else
                                                {
                                                    throw new InvalidOperationException($"For less than {leaveType.DaysCheck} or {leaveType.DaysCheck} {leaveType.TypeName}, leave should be applied atleast {leaveType.DaysCheckEqualOrLess} days before. ");
                                                }
                                            }
                                        }
                                        else
                                        {
                                            throw new InvalidOperationException("Days Check Not Assinged");
                                        }
                                    }
                                    else
                                    {
                                        throw new InvalidOperationException("Half Day can Only Be Applied For One Day");
                                    }
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Half Day Not Applicable for {leaveType.TypeName}");
                                }
                            }

                        }
                        else
                        {
                            return await Balance(leaveBalance.Balance,leaveType.TypeName);
                        }

                    }
                    else if (currentDate.DayNumber >= request.FromDate.DayNumber)
                    {
                        if (leaveBalance.Balance >= days && virtualBalance >= days)
                        {
                            if(!request.HalfDay.HasValue || request.HalfDay == false) 
                            {
                                return await PreviousDayLeaves();
                            }
                            else
                            {
                                if (leaveType.IsHalfDayAllowed == true)
                                {
                                    if (leaveDaysResultDto.DaysCount == 1)
                                    {
                                        return await PreviousDayLeaves();
                                    }
                                    else
                                    {
                                        throw new InvalidOperationException("Half Day can Only Be Applied For One Day");
                                    }
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Half Day Not Applicable for {leaveType.TypeName}");
                                }
                                 
                            }
                            

                        }
                        else
                        {
                            return await Balance(leaveBalance.Balance,leaveType.TypeName);
                        }

                    }
                    else
                    {
                        throw new InvalidOperationException($"Current Leave Cannot Be Applied for Selected Dates");
                    }
                }
                else
                {
                    if (!employee.Gender.HasValue)
                    {
                        throw new InvalidOperationException($"Complete Profile Details Before Applying Leave.Mainly Gender");

                    }
                    throw new InvalidOperationException($"The Leave You Applied is gender Specific And You Cannot Apply For {leaveType.TypeName}");
                }
            }
            else
            {
                throw new InvalidOperationException($"One Of the Date in Applied Dates is Already Applied");
            }

            

            

            async Task<bool> AddLeave()
            {
                if (days > 0) {
                    LeaveRequest leaveRequest = new LeaveRequest(request.EmpId, request.TypeId, request.HalfDay, request.FromDate, request.ToDate, currentDate, LeaveRequestStatus.Open, request.Description, request.EmpId);
                    _context.LeaveRequests.Add(leaveRequest);
                    await _context.SaveChangesAsync(cancellationToken);
                    AddLeaveRequestDayCommand addLeaveRequestDayCommand = new AddLeaveRequestDayCommand();
                    addLeaveRequestDayCommand.LeaveRequestId = leaveRequest.Id;
                    addLeaveRequestDayCommand.Date = dates;
                    List<string> receiverManagerEmails = await _emailFinder.FindManagerEmailsAsync(employee.ManagerId);
                    string[] immediateManagerMail = [];
                    string[]? superiorsMails = null;
                    if (rolesString == "SuperAdmin")
                    {
                         immediateManagerMail =  new string[] { employee.Email};;
                    }
                    else
                    {
                         immediateManagerMail = new string[] {receiverManagerEmails[0]};
                        superiorsMails = receiverManagerEmails.Skip(1).ToArray();
                    }
                    string subject = $"Leave Application for dates {request.FromDate} to &{request.ToDate}";
                    string message = $@"
                                    <html>
                                    <body>

                                        <p>
                                            I hope this message finds you well. This is an automated leave Application mail for {employee.FirstName} {employee.LastName}
                                        </p>

                                        <table border='1' style='border-collapse: collapse; width: 100%;'>
                                            <tr>
                                                <th style='padding: 8px; text-align: left; border: 1px solid #ddd;'>Information</th> 
                                                <th style='padding: 8px; text-align: left; border: 1px solid #ddd;'>Value</th> 
                                            </tr>
                                            <tr>
                                                <td style='padding: 8px; border: 1px solid #ddd;'>Employee Name</td> 
                                                <td style='padding: 8px; border: 1px solid #ddd;'>{employee.FirstName} {employee.LastName}</td> 
                                            </tr>
                                            <tr>
                                                <td style='padding: 8px; border: 1px solid #ddd;'>Employee Code</td> 
                                                <td style='padding: 8px; border: 1px solid #ddd;'>{employee.EmployeeCode}</td> 
                                            </tr>
                                            <tr>
                                                <td style='padding: 8px; border: 1px solid #ddd;'>Employee Phone</td> 
                                                <td style='padding: 8px; border: 1px solid #ddd;'>{employee.PhoneNumber}</td> 
                                            </tr>
                                            <tr>
                                                <td style='padding: 8px; border: 1px solid #ddd;'>Leave Type</td> 
                                                <td style='padding: 8px; border: 1px solid #ddd;'>{((leaveType.LeaveCategoryId == LeaveCategory.PrivilegeLeave || (leaveType.LeaveCategoryId == LeaveCategory.CasualLeave && currentDate.DayNumber >= request.FromDate.DayNumber)) ? "Emergency Leave" : leaveType.TypeName)}</td>  
                                            </tr>
                                            <tr>
                                                <td style='padding: 8px; border: 1px solid #ddd;'>From Date</td> 
                                                <td style='padding: 8px; border: 1px solid #ddd;'>{request.FromDate}</td> 
                                            </tr>
                                            <tr>
                                                <td style='padding: 8px; border: 1px solid #ddd;'>To Date</td> 
                                                <td style='padding: 8px; border: 1px solid #ddd;'>{request.ToDate}</td> 
                                            </tr>
                                            <tr>
                                                <td style='padding: 8px; border: 1px solid #ddd;'>Leave Description</td> 
                                                <td style='padding: 8px; border: 1px solid #ddd;'>{request.Description}</td> 
                                            </tr>
                                        </table>

                                    </body>
                                    </html>";

                     _email.SendMail(_senderEmail, _senderName, immediateManagerMail, message, subject, superiorsMails);
                    List<int> managerIds = await FindManagerIdsAsync(employee.ManagerId, cancellationToken);
                    string notificationMessage = $" Leave {leaveRequest.FromDate} to {leaveRequest.ToDate} is Applied by {employee.FirstName} {employee.LastName} [Employee Code :{employee.EmployeeCode}]";
                    foreach(int managerId in managerIds)
                    {
                        NotificationCommand command = new NotificationCommand
                        {
                            EmployeeIds = new List<int> { managerId },
                            Message = notificationMessage,
                        };
                        await _mediator.Send(command, cancellationToken);
                    }
                    
                    return await _mediator.Send(addLeaveRequestDayCommand, cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException("Total Leave days are 0");
                }
                

            }

            async Task<bool> CheckOne()
            {
                if (leaveType.DutyDaysRequired.HasValue)
                {
                    if (employee.JoiningDate.HasValue)
                    {
                        if (employee.JoiningDate.HasValue && (request.FromDate.DayNumber - employee.JoiningDate.Value.DayNumber >= leaveType.DutyDaysRequired))
                        {
                            if (leaveType.LeaveCategoryId.HasValue && (leaveType.LeaveCategoryId == LeaveCategory.RestrictedHoliday))
                            {
                                int restrictedHolidayCount = await _context.Holiday.Where(x => x.Type == AttendanceStatus.RestrictedHoliday && x.Date >= request.FromDate && x.Date <= request.ToDate).CountAsync();
                                if (days == restrictedHolidayCount)
                                {
                                    return await AddLeave();
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Selected Day Do not Contain Restricted Holiday");
                                }
                            }
                            else
                            {
                                return await AddLeave();
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException($"Minimum Duty Days of {leaveType.DutyDaysRequired} is Required for {leaveType.TypeName} .");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Joining date Not assinged by HR.");
                    }
                }
                else
                {
                    if (leaveType.LeaveCategoryId.HasValue && (leaveType.LeaveCategoryId == LeaveCategory.RestrictedHoliday))
                    {
                        int restrictedHolidayCount = await _context.Holiday.Where(x => x.Type == AttendanceStatus.RestrictedHoliday && x.Date >= request.FromDate && x.Date <= request.ToDate).CountAsync();
                        if (days == restrictedHolidayCount)
                        {
                            return await AddLeave();
                        }
                        else
                        {
                            throw new InvalidOperationException($"Selected Day Do not Contain Restricted Holiday");
                        }
                    }
                    else
                    {
                        return await AddLeave();
                    }
                }
            }


            async Task<bool> WorkFromHomeCheck()
            {
                if (leaveType.DutyDaysRequired.HasValue)
                {
                    if (employee.JoiningDate.HasValue)
                    {
                        if ((request.FromDate.DayNumber - employee.JoiningDate.Value.DayNumber >= leaveType.DutyDaysRequired))
                        {
                            return await AddLeave();
                        }
                        else
                        {
                            throw new InvalidOperationException($"Minimum Duty Days of {leaveType.DutyDaysRequired} is Required for {leaveType.TypeName} .");
                        }

                    }
                    else
                    {
                        throw new InvalidOperationException($"Joining date Not assinged by HR.");
                    }
                }
                else
                {
                    return await AddLeave();
                }
            }


            
            async Task<bool> PreviousDayLeaves()
            {
                if (leaveType.LeaveCategoryId == LeaveCategory.BereavementLeave)
                {
                    return await AddLeave();
                }
                else if (leaveType.LeaveCategoryId == LeaveCategory.PrivilegeLeave || leaveType.LeaveCategoryId == LeaveCategory.CasualLeave)
                {
                    LeaveType EmergencyLeave = await _context.LeaveTypes.Where(x => x.LeaveCategoryId == LeaveCategory.EmergencyLeave).FirstOrDefaultAsync(cancellationToken);
                    LeaveBalance leaveBalance2 = await _context.LeaveBalances.FirstOrDefaultAsync(x => x.EmployeeId == request.EmpId && x.TypeId == EmergencyLeave.Id, cancellationToken);
                    decimal EmergencyVirtualLeaveCountWithOutHalfDay = await _context.LeaveRequestDays.Where(x => x.LeaveRequest.EmployeeId == request.EmpId && x.LeaveRequest.TypeId == request.TypeId && x.LeaveRequest.LeaveRequestStatusId == LeaveRequestStatus.Open && x.LeaveRequest.ApplyDate >= x.LeaveRequest.FromDate && x.LeaveRequest.HalfDay != true ).CountAsync(cancellationToken);
                    decimal EmergencyVirtualLeaveCountWithHalfDay = await _context.LeaveRequestDays.Where(x => x.LeaveRequest.EmployeeId == request.EmpId && x.LeaveRequest.TypeId == request.TypeId && x.LeaveRequest.LeaveRequestStatusId == LeaveRequestStatus.Open && x.LeaveRequest.ApplyDate >= x.LeaveRequest.FromDate && x.LeaveRequest.HalfDay == true ).CountAsync(cancellationToken);
                    decimal EmergencyVirtualBalance = leaveBalance.Balance - (virtualLeaveCountWithoutHalfDay + (virtualLeaveCountWithHalfDay / 2));
                    if (leaveBalance2.Balance >= days && EmergencyVirtualBalance >= days)
                    {
                        return await AddLeave();
                    }
                    else
                    {
                        return await Balance(leaveBalance2.Balance, EmergencyLeave.TypeName);
                    }

                }
                else
                {
                    throw new InvalidOperationException($"Applying Leave For Previous Day is only Possible for Emergency  And .Bereavement Leave . And Half Day is not Applicable ");
                }
            }

            async Task<bool> Balance(decimal balance, string name)
            {
                if (balance < days)
                {
                    throw new InvalidOperationException($"No Sufficient Leave for type {name}");
                }
                else
                {
                    throw new InvalidOperationException($"Revoke or edit existing {name}. All Balances are taken by applied leaves");
                }
            }

             async Task<List<int>> FindManagerIdsAsync(int? managerId, CancellationToken cancellationToken)
            {
                List<int> managerIds = new List<int>();
                if (managerId is null)
                    return managerIds;
                Employee? manager = await _context.Employees
                    .Where(m => m.Id == managerId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (manager is not null)
                {
                    managerIds.Add(manager.Id);
                    List<int> higherManagerIds = await FindManagerIdsAsync(manager.ManagerId, cancellationToken);
                    managerIds.AddRange(higherManagerIds);
                }
                return managerIds;
            }


        }
    }
}
