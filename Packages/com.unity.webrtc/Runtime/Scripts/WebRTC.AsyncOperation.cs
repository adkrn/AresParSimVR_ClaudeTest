using System;
using UnityEngine;

namespace Unity.WebRTC
{
    /// <summary>
    /// 
    /// </summary>
    public class AsyncOperationBase : CustomYieldInstruction
    {
        /// <summary>
        /// 
        /// </summary>
        public RTCError Error { get; internal set; }

        /// <summary>
        /// 
        /// </summary>
        public bool IsError { get; internal set; }

        /// <summary>
        /// 
        /// </summary>
        public bool IsDone { get; internal set; }

        /// <summary>
        /// 
        /// </summary>
        public override bool keepWaiting
        {
            get
            {
                if (IsDone)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        internal void Done()
        {
            IsDone = true;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class RTCStatsReportAsyncOperation : AsyncOperationBase
    {
        /// <summary>
        /// 
        /// </summary>
        public RTCStatsReport Value { get; private set; }

        internal RTCStatsReportAsyncOperation(RTCStatsCollectorCallback callback)
        {
            callback.onStatsDelivered = OnStatsDelivered;
        }

        void OnStatsDelivered(RTCStatsReport report)
        {
            Value = report;
            IsError = false;
            this.Done();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class RTCSessionDescriptionAsyncOperation : AsyncOperationBase
    {
        /// <summary>
        /// 
        /// </summary>
        public RTCSessionDescription Desc { get; internal set; }
        
        private Action<RTCSessionDescription> onComplete;

        internal RTCSessionDescriptionAsyncOperation(CreateSessionDescriptionObserver observer)
        {
            observer.onCreateSessionDescription = OnCreateSessionDescription;
        }

        void OnCreateSessionDescription(RTCSdpType type, string sdp, RTCErrorType errorType, string error)
        {
            IsError = errorType != RTCErrorType.None;
            Error = new RTCError() { errorType = errorType, message = error };
            Desc = new RTCSessionDescription() { type = type, sdp = sdp };
            this.Done();
            
            // 작업 완료 시 콜백 실행
            onComplete?.Invoke(Desc);
        }
        
        public RTCSessionDescriptionAsyncOperation Then(Action<RTCSessionDescription> action)
        {
            if (IsDone)
            {
                // 작업이 이미 완료된 경우 즉시 실행
                action?.Invoke(Desc);
            }
            else
            {
                // 작업 완료 후 실행될 콜백 저장
                onComplete = action;
            }
            return this;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class RTCSetSessionDescriptionAsyncOperation : AsyncOperationBase
    {
        internal RTCSetSessionDescriptionAsyncOperation(SetSessionDescriptionObserver observer)
        {
            observer.onSetSessionDescription = OnSetSessionDescription;
        }

        void OnSetSessionDescription(RTCErrorType errorType, string error)
        {
            IsError = errorType != RTCErrorType.None;
            Error = new RTCError() { errorType = errorType, message = error };
            this.Done();
        }
    }
}
