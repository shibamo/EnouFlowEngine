
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

using EnouFlowTemplateLib;
using EnouFlowInstanceLib;
using EnouFlowInstanceLib.Actions;
using EnouFlowOrgMgmtLib;

namespace EnouFlowEngine
{
  public partial class FlowEngine
  {
    public FlowActionRejectToStartResult // rejectToStart
      processActionRequest(FlowActionRejectToStart req)
    {
      var concreteMetaObj = req.concreteMetaObj;

      using (var db = new EnouFlowInstanceContext())
      {
        var flowInst = getFlowInstance(db, req.flowInstanceId, req.bizDocumentGuid);

        var reqInDb = getReqInDB(req.flowActionRequestId, db);
        string failReason;
        ActivityNode destNode;

        #region Check BizTimeStamp Valid
        if (!isBizTimeStampValid((DateTime)concreteMetaObj.bizTimeStamp,
          req, flowInst, out failReason))
        {
          updateReqProcessingResultInDB(reqInDb,
            EnumFlowActionRequestResultType.fail, failReason);
          db.SaveChanges();

          return new FlowActionRejectToStartResult(req.flowActionRequestId,
            req.clientRequestGuid, flowInst, false, failReason);
        }
        #endregion

        #region Decide next activity, need be start type
        var flowDefHelper = new FlowTemplateDefHelper(
          flowInst.flowTemplateJson);
        string startActivityGuid;
        if (string.IsNullOrWhiteSpace(req.startActivityGuid))
        {
          startActivityGuid = flowInst.startActivityGuid;
        }
        else
        {
          startActivityGuid = req.startActivityGuid;
        }
        destNode = flowDefHelper.getNodeFromGuid(startActivityGuid);

        if (destNode.type != ActivityTypeString.standard_Start)
        {
          failReason = "目标活动不是开始类型(FlowActionRejectToStartResult)";
          updateReqProcessingResultInDB(reqInDb,
            EnumFlowActionRequestResultType.fail, failReason);
          db.SaveChanges();

          return new FlowActionRejectToStartResult(req.flowActionRequestId,
            req.clientRequestGuid, flowInst, false, failReason);
        }
        #endregion

        #region Decide activity owners/ List<UserDTO>
        List<UserDTO> taskUsers = new List<UserDTO>();
        // taskUsers = FlowTemplateDefHelper.getUserDTOsFromPaticipantList(req.roles);
        taskUsers = getUserDTOsFromPaticipantList(req.roles, flowInst);

        if (taskUsers.Count() == 0)
        {// 如果没有直接指定用户, 则根据流程实例的creatorId,将任务指派给流程实例的创建者
          using (var orgDb = new EnouFlowOrgMgmtContext())
          {
            var creator = OrgMgmtDBHelper.getUserDTO(flowInst.creatorId, orgDb);
            if (creator != null)
            {
              taskUsers.Add(creator);
            }
          }
        }

        if (taskUsers.Count() == 0) // 如果参与活动的用户数为0则出错
        {
          failReason = $@"无法找到参与活动'{destNode.name}'的用户" +
                        $@"({ req.concreteMetaObj.roles}). (FlowActionRejectToStartResult)";

          updateReqProcessingResultInDB(reqInDb,
            EnumFlowActionRequestResultType.fail, failReason);
          db.SaveChanges();

          return new FlowActionRejectToStartResult(req.flowActionRequestId,
           req.clientRequestGuid, flowInst, false, failReason);
        }
        #endregion

        #region  update instance
        DateTime newBizTimeStamp = DateTime.Now;
        DateTime originBizTimeStamp = flowInst.bizTimeStamp;
        flowInst.bizTimeStamp = newBizTimeStamp;
        flowInst.previousActivityGuid = flowInst.currentActivityGuid;
        flowInst.previousActivityName = flowInst.currentActivityName;
        flowInst.currentActivityGuid = destNode.guid;
        flowInst.currentActivityName = destNode.name;
        // RejectToStart不更新BizDataPayloadJson:
        // updateBizDataPayloadJsonOfFlowInst(flowInst, req);
        #endregion

        #region  update tasks for user status like taskState,finishTime
        updateTaskForUserStatesAfterAction(db, (int)concreteMetaObj.userId,
          originBizTimeStamp, newBizTimeStamp,
          flowInst.previousActivityGuid, flowInst, reqInDb);
        #endregion

        #region  add task for users: FlowTaskForUser
        taskUsers.ForEach(user => addFlowTaskForUser(db, user, flowInst, EnumFlowTaskType.redraft));
        #endregion

        #region  write 3 type logs: FlowInstanceFriendlyLog & FlowInstanceTechLog
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

        db.SaveChanges();

        return new FlowActionRejectToStartResult(req.flowActionRequestId,
          req.clientRequestGuid, flowInst);

      }
    }
  }
}

