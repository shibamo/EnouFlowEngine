using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

using EnouFlowTemplateLib;
using EnouFlowInstanceLib;
using EnouFlowOrgMgmtLib;
using EnouFlowInstanceLib.Actions;

namespace EnouFlowEngine
{
  public partial class FlowEngine
  {
    public FlowActionInviteOtherFeedbackResult
      processActionRequest(FlowActionInviteOtherFeedback req)
    {
      var concreteMetaObj = req.concreteMetaObj;

      using (var db = new EnouFlowInstanceContext())
      {
        var flowInst = getFlowInstance(db, req.flowInstanceId, req.bizDocumentGuid);

        var reqInDb = getReqInDB(req.flowActionRequestId, db);
        string failReason;
        // 此类型ActionRequest不改TimeStamp
        DateTime bizTimeStampToUse = flowInst.bizTimeStamp;

        #region Check BizTimeStamp Valid
        if (!isBizTimeStampValid((DateTime)concreteMetaObj.bizTimeStamp,
          req, flowInst, out failReason))
        {
          updateReqProcessingResultInDB(reqInDb,
            EnumFlowActionRequestResultType.fail, failReason);
          db.SaveChanges();

          return new FlowActionInviteOtherFeedbackResult(req.flowActionRequestId,
            req.clientRequestGuid, flowInst, false, failReason);
        }
        #endregion

        #region Decide List<UserDTO>
        // 需要追溯到最初发出征询意见的用户作为任务目标用户
        List<UserDTO> taskUsers = new List<UserDTO>();
        var taskInviteOther = db.flowTaskForUsers.Find(
          req.relativeFlowTaskForUserId);
        var flowTaskForUserOrigin = db.flowTaskForUsers.Find(
          taskInviteOther.relativeFlowTaskForUserId);
        using (var orgDb = new EnouFlowOrgMgmtContext())
        {
          taskUsers.Add(OrgMgmtDBHelper.getUserDTO(
          flowTaskForUserOrigin.userId, orgDb));
        }
        #endregion

        #region  add the invitation-feedback "task" for users
        FlowTemplateDefHelper flowTemplateDefHelper = new 
          FlowTemplateDefHelper(flowInst.flowTemplateJson);
        string suggestedConnectionName = "";
        if(!string.IsNullOrWhiteSpace(req.connectionGuid))
        {
          suggestedConnectionName = 
            flowTemplateDefHelper.getNodesOfConnection(
              req.connectionGuid).Item3.name;
        }
        string suggestedPaticipants = "";
        if (req.roles.Count() > 0)
        {
          suggestedPaticipants = req.roles.Aggregate(
            "", (names, role) => {
              if (role != null)
              { 
                return names + role.PaticipantObj.name + ";";
              }
              else
              {
                return names;
              }
            });
        }
        taskUsers.ForEach(user => {
          var task = addFlowTaskForUser(
            db, user, flowInst, EnumFlowTaskType.invitationFeedback);
          // 设置任务之间的跟踪关系
          task.relativeFlowTaskForUserId = req.relativeFlowTaskForUserId;
          // 设置征询意见反馈型任务的自定义字段
          task.stringField_1 = req.userMemo;
          task.stringField_2 = suggestedConnectionName;
          task.stringField_3 = suggestedPaticipants;
        });
        // 顺便把原征询意见的任务也一并更新
        taskInviteOther.stringField_1 = req.userMemo;
        taskInviteOther.stringField_2 = suggestedConnectionName;
        taskInviteOther.stringField_3 = suggestedPaticipants;
        taskInviteOther.finishTime = DateTime.Now;
        taskInviteOther.taskState = EnumFlowTaskState.done;
        #endregion

        #region  write 3 type logs
        // FlowInstanceFriendlyLog & FlowInstanceTechLog
        var friendlyLog = db.flowFriendlyLogs.Create();
        friendlyLog.flowInstance = flowInst;
        friendlyLog.flowInstanceGuid = flowInst.guid;
        friendlyLog.flowActionRequestId = req.flowActionRequestId;
        db.flowFriendlyLogs.Add(friendlyLog);
#warning TODO: another 2 type logs
        #endregion

        #region  update request
        updateRequestToSuccess(reqInDb, flowInst);
        #endregion

        #region  save all to db
        db.SaveChanges();
        #endregion

        return new FlowActionInviteOtherFeedbackResult(req.flowActionRequestId,
          req.clientRequestGuid, flowInst);
      }
    }
  }
}
