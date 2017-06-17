﻿using System;
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
    public FlowActionMoveToAutoGeneratedResult // moveToAutoGenerated
      processActionRequest(FlowActionMoveToAutoGenerated req)
    {
      var concreteMetaObj = req.concreteMetaObj;

      using (var db = new EnouFlowInstanceContext())
      {
        var flowInst = getFlowInstance(db, req.flowInstanceId, req.bizDocumentGuid);

        var reqInDb = getReqInDB(req.flowActionRequestId, db);
        Tuple<ActivityNode, ActivityNode, ActivityConnection> from_to_conn;
        string failReason;

        #region Check BizTimeStamp Valid 自动型跳过检查
        //if (!isBizTimeStampValid((DateTime)concreteMetaObj.bizTimeStamp,
        //  req, flowInst, db, out failReason))
        //{
        //  updateReqProcessingResultInDB(reqInDb,
        //    EnumFlowActionRequestResultType.fail, failReason);
        //  db.SaveChanges();

        //  return new FlowActionMoveToAutoGeneratedResult(req.flowActionRequestId,
        //    req.clientRequestGuid, flowInst, false, failReason);
        //}
        #endregion

        #region Decide next activity, switch different activity type
        var flowDefHelper = new FlowTemplateDefHelper(
          flowInst.flowTemplateJson);
        from_to_conn = flowDefHelper.getNodesOfConnection(req.connectionGuid);

        #region 验证流程实例当前所处的状态能够使用该connection
        if (from_to_conn.Item1.guid != flowInst.currentActivityGuid)
        {
          failReason = $"当前所处的状态{from_to_conn.Item1.name}" +
            $"不支持使用连接{from_to_conn.Item3.name}";

          updateReqProcessingResultInDB(reqInDb,
            EnumFlowActionRequestResultType.fail, failReason);
          db.SaveChanges();

          return new FlowActionMoveToAutoGeneratedResult(req.flowActionRequestId,
           req.clientRequestGuid, flowInst, false, failReason);
        }
        #endregion

        #endregion

        #region 目的状态是自动类型时需要根据条件为该活动自动生成接续的对应FlowActionRequest
        if (from_to_conn.Item2.type == ActivityTypeString.standard_Auto)
        {
          var _toActivity = from_to_conn.Item2; // 目标自动活动
          var _autoResult = ExecuteAutoRulesAsync(_toActivity.autoRules, req.bizDataPayloadJson,
            req.optionalFlowActionDataJson, flowInst).Result;
          var _effectiveConnectionGuid = _autoResult.Item1;
          var _paticipantsOfAutoRules = _autoResult.Item2;

          //根据自动活动规则集的运行结果由引擎Post相应的MoveToAutoGenerated型处理请求
          FlowActionHelper.PostFlowActionMoveToAutoGenerated(
            Guid.NewGuid().ToString(), req.bizDocumentGuid, req.bizDocumentTypeCode, 
            DateTime.Now, "自动活动规则生成",
            req.bizDataPayloadJson, req.optionalFlowActionDataJson,
            flowInst.flowInstanceId, flowInst.guid, flowInst.code,
            from_to_conn.Item2.guid, _effectiveConnectionGuid,
            flowDefHelper.getNodesOfConnection(
              _effectiveConnectionGuid).Item2.guid,
            _paticipantsOfAutoRules, db);
        }
        #endregion

        #region Decide activity owners/ List<UserDTO>
        List<UserDTO> taskUsers = new List<UserDTO>();
        switch (from_to_conn.Item2.type)
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
              failReason = $"无法找到参与活动'{from_to_conn.Item2.name}'" +
                $"的用户({req.roles.ToString()}).";

              updateReqProcessingResultInDB(reqInDb,
                EnumFlowActionRequestResultType.fail, failReason);
              db.SaveChanges();

              return new FlowActionMoveToAutoGeneratedResult(req.flowActionRequestId,
               req.clientRequestGuid, flowInst, false, failReason);
            }

            break;

          case ActivityTypeString.standard_Auto:
            // 目标活动状态为自动,暂定不设置activity owner
            break;
          default:
            throw new EnouFlowInstanceLib.DataLogicException(
              $"遇到未定义处理方式的活动类型: {from_to_conn.Item2.type}");
        }
        #endregion

        #region  update instance
        switch (from_to_conn.Item2.type)
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
              $"遇到未定义处理方式的活动类型: {from_to_conn.Item2.type}");
        }
        flowInst.bizTimeStamp = DateTime.Now;
        flowInst.currentActivityGuid = from_to_conn.Item2.guid;
        flowInst.currentActivityName = from_to_conn.Item2.name;
        flowInst.previousActivityGuid = from_to_conn.Item1.guid;
        flowInst.previousActivityName = from_to_conn.Item1.name;
        updateBizDataPayloadJsonOfFlowInst(flowInst, req);

        #endregion

        #region Create request automatically for auto activity

        #endregion

        #region  update tasks for user status like taskState,finishTime
        //自动型不需要做该动作,因为FromNode是自动型,应该无人工任务产生
        #endregion

        #region  add task for users: FlowTaskForUser
        taskUsers.ForEach(user => addFlowTaskForUser(db, user, flowInst));
        #endregion

        #region  write 3 type logs: FlowInstanceFriendlyLog & FlowInstanceTechLog

        #endregion

        #region  update request
        updateRequestToSuccess(reqInDb, flowInst);
        #endregion

        #region  save all to db
        db.SaveChanges();
        #endregion

        return new FlowActionMoveToAutoGeneratedResult(req.flowActionRequestId,
          req.clientRequestGuid, flowInst);
      }


    }
  }
}
