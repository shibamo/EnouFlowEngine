﻿using System;
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
    public FlowActionJumpToResult // jumpTo
    processActionRequest(FlowActionJumpTo req)
    {
      var concreteMetaObj = req.concreteMetaObj;

      using (var db = new EnouFlowInstanceContext())
      {
        var flowInst = getFlowInstance(db, req.flowInstanceId, req.bizDocumentGuid);

        var reqInDb = getReqInDB(req.flowActionRequestId, db);
        string failReason;
        DateTime bizTimeStampToUse = DateTime.Now;

        #region Check BizTimeStamp Valid
        if ( !req.forceJump &&
          !isBizTimeStampValid(
            (DateTime)concreteMetaObj.bizTimeStamp,
            req, flowInst, out failReason))
        {
          updateReqProcessingResultInDB(reqInDb,
            EnumFlowActionRequestResultType.fail, failReason);
          db.SaveChanges();

          return new FlowActionJumpToResult(req.flowActionRequestId,
            req.clientRequestGuid, flowInst, false, failReason);
        }
        #endregion

        var flowDefHelper = new FlowTemplateDefHelper(
          flowInst.flowTemplateJson);
        var toActivity = flowDefHelper.getNodeFromGuid(req.nextActivityGuid);
        var currentActivity = flowDefHelper.getNodeFromGuid(req.currentActivityGuid);

        #region 目的状态是自动类型时需要根据条件为该活动自动生成接续的对应FlowActionRequest
        if (toActivity.type == ActivityTypeString.standard_Auto)
        {
          //var _toActivity = toActivity; // 目标自动活动
          var _autoResult = ExecuteAutoRulesAsync(toActivity.autoRules,
            req.bizDataPayloadJson, req.optionalFlowActionDataJson, flowInst).Result;
          var _effectiveConnectionGuid = _autoResult.Item1;
          var _paticipantsOfAutoRules = _autoResult.Item2;

          // 根据自动活动规则集的运行结果由引擎Post相应的MoveToAutoGenerated型处理请求
          // 但不马上处理,由客户端或者流程引擎后台调度器自动调用处理
          FlowActionHelper.PostFlowActionMoveToAutoGenerated(
            Guid.NewGuid().ToString(), req.bizDocumentGuid, req.bizDocumentTypeCode,
            DateTime.Now, "自动活动规则生成", req.bizDataPayloadJson,
            req.optionalFlowActionDataJson, flowInst.flowInstanceId, flowInst.guid,
            flowInst.code, toActivity.guid, _effectiveConnectionGuid,
            flowDefHelper.getNodesOfConnection(_effectiveConnectionGuid).Item2.guid,
            _paticipantsOfAutoRules, db);
        }
        #endregion

        #region Decide activity owners/ List<UserDTO>
        List<UserDTO> taskUsers = new List<UserDTO>();
        switch (toActivity.type)
        {
          case ActivityTypeString.standard_End: // 目标活动状态为结束,不需要设置activity owner, 是否需要有最终收尾处理的人 ???
            break;
          case ActivityTypeString.standard_Start: // 下面这三类目标活动状态需要设置activity owner
          case ActivityTypeString.standard_SingleHuman:
          case ActivityTypeString.standard_MultiHuman:
            // taskUsers = FlowTemplateDefHelper.getUserDTOsFromPaticipantList(req.roles);
            taskUsers = getUserDTOsFromPaticipantList(req.roles, flowInst);

            if (taskUsers.Count() == 0) // 如果参与活动的用户数为0则出错
            {
              failReason = $"无法找到参与活动'{toActivity.name}'" +
                $"的用户({req.roles.ToString()}).";

              updateReqProcessingResultInDB(reqInDb,
                EnumFlowActionRequestResultType.fail, failReason);
              db.SaveChanges();

              return new FlowActionJumpToResult(req.flowActionRequestId,
               req.clientRequestGuid, flowInst, false, failReason);
            }

            break;

          case ActivityTypeString.standard_Auto:
            // 目标活动状态为自动,暂定不设置activity owner
            break;
          default:
            throw new EnouFlowInstanceLib.DataLogicException(
              $"遇到未定义处理方式的活动类型: {toActivity.type}");
        }
        #endregion

        #region  update instance
        switch (toActivity.type)
        {
          case ActivityTypeString.standard_End:
            flowInst.lifeState = EnumFlowInstanceLifeState.end;
            break;

          case ActivityTypeString.standard_Start:
            flowInst.lifeState = EnumFlowInstanceLifeState.start;
            break;

          case ActivityTypeString.standard_SingleHuman:
          case ActivityTypeString.standard_MultiHuman:
          case ActivityTypeString.standard_Auto:
            flowInst.lifeState = EnumFlowInstanceLifeState.middle;
            break;

          default:
            throw new EnouFlowInstanceLib.DataLogicException(
              $"遇到未定义处理方式的活动类型: {toActivity.type}");
        }
        var originBizTimeStamp = flowInst.bizTimeStamp;
        flowInst.bizTimeStamp = bizTimeStampToUse;
        flowInst.currentActivityGuid = toActivity.guid;
        flowInst.currentActivityName = toActivity.name;
        flowInst.previousActivityGuid = currentActivity.guid;
        flowInst.previousActivityName = currentActivity.name;
        updateBizDataPayloadJsonOfFlowInst(flowInst, req);

        #endregion

        #region  update tasks for user status like taskState,finishTime
        updateTaskForUserStatesAfterAction(db, (int)concreteMetaObj.userId,
          originBizTimeStamp, bizTimeStampToUse,
          flowInst.previousActivityGuid, flowInst, reqInDb);
        #endregion

        #region  add task for users: FlowTaskForUser
        taskUsers.ForEach(user => addFlowTaskForUser(db, user, flowInst));
        #endregion

        #region  write 3 type logs: FlowInstanceFriendlyLog & FlowInstanceTechLog
        addFlowInstanceFriendlyLog(
          flowInst, reqInDb.flowActionRequestId, flowInst.previousActivityName,
          reqInDb.userId.Value, reqInDb.delegateeUserId,
          "跳转/Jump", req.userMemo, db);
#warning TODO: another 2 type logs
        #endregion

        #region  update request
        updateRequestToSuccess(reqInDb, flowInst);
        #endregion

        #region  save all to db
        db.SaveChanges();
        #endregion

        return new FlowActionJumpToResult(req.flowActionRequestId,
          req.clientRequestGuid, flowInst);
      }


    }

    

  }
}
