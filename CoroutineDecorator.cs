using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ProjectZero
{
	public class StopIteration:System.Exception
	{

	}

	public class ITask: object
	{
	}

	public class CoroutineTask:ITask
	{
		private static int _nextTaskId = 0;
		private int _taskId;
		private bool _stopped;
		public IEnumerator target;
		public object sendval;
		private Stack<IEnumerator> _stack;

		public int taskId
		{
			get
			{
				return _taskId;
			}
		}

		public CoroutineTask(IEnumerator target)
		{
			_taskId = ++_nextTaskId;
			this.target = target;
			sendval = null;
			_stopped = false;
			_stack = new Stack<IEnumerator>();
		}

		public void Stop()
		{
			_stopped = true;
		}

		public SystemTask Run()
		{
			if(_stopped)
				throw new StopIteration();
			while (true) {
				Singleton<CoroutineDecorator>.Instance.returnValueRegister = sendval;
				if(target.MoveNext())
				{
					object result = target.Current;
					if(result != null && result is SystemTask)
					{
						return result as SystemTask;
					}
					else if(result != null && result is IEnumerator)
					{
						_stack.Push(target);
						sendval = null;
						target = (IEnumerator)result;
					}
					else
					{
						if(_stack.Count <= 0)
							return null;
						sendval = result;
						target = _stack.Pop();
					}
				}
				else 
				{
					if(_stack.Count <= 0)
						throw new StopIteration();
					sendval = null;
					target = _stack.Pop();
				}
			}
		}
	}

	public class SystemTask : ITask
	{
		public CoroutineTask task;
		public virtual void Handle()
		{
			Singleton<CoroutineDecorator>.Instance.Schedule (task.taskId);
		}
	}

	public class GetTid: SystemTask
	{
		public override void Handle()
		{
			task.sendval = task.taskId;
			Singleton<CoroutineDecorator>.Instance.Schedule (task.taskId);
		}
	}

	public class NewTask: SystemTask
	{
		IEnumerator _target;

		public NewTask(IEnumerator task)
		{
			_target = task;
		}

		public override void Handle()
		{
			int tid = Singleton<CoroutineDecorator>.Instance.New (_target);
			task.sendval = tid;
			Singleton<CoroutineDecorator>.Instance.Schedule (task.taskId);
		}
	}

	public class KillTask: SystemTask
	{
		int _taskId;

		public KillTask(int taskId)
		{
			_taskId = taskId;
		}

		public override void Handle()
		{
			CoroutineTask task = Singleton<CoroutineDecorator>.Instance.getCoroutineTask (_taskId);
			if (task != null) {
				task.Stop();
			}
			Singleton<CoroutineDecorator>.Instance.Schedule (task.taskId);
		}
	}

	class WaitTask: SystemTask
	{
		int _taskId;
		public WaitTask(int taskId)
		{
			_taskId = taskId;
		}

		public override void Handle()
		{
			bool result = Singleton<CoroutineDecorator>.Instance.WaitForExit (task, _taskId);
			if (!result)
				Singleton<CoroutineDecorator>.Instance.Schedule (task.taskId);
		}
	}

	class HangUp: SystemTask
	{
		public HangUp()
		{

		}

		public override void Handle()
		{

		}
	}

	class Lock: SystemTask
	{
		private int _lockId;
		public Lock(int lockId)
		{
			_lockId = lockId;
		}
		
		public override void Handle()
		{
			if (Singleton<CoroutineDecorator>.Instance.AquireLock (_lockId, task.taskId)) {
				Singleton<CoroutineDecorator>.Instance.Schedule(task.taskId);
			}
		}
	}

	class UnLock: SystemTask
	{
		private int _lockId;
		public UnLock(int lockId)
		{
			_lockId = lockId;
		}
		
		public override void Handle()
		{
			Singleton<CoroutineDecorator>.Instance.ReleaseLock (_lockId);
			Singleton<CoroutineDecorator>.Instance.Schedule (task.taskId);
		}
	}

	public class WaitForTime
	{
		private float _delay;

		public WaitForTime(float delay)
		{
			_delay = delay;
		}

		public IEnumerator DelayTask()
		{
			float startTime = Time.time;
			while (Time.time - startTime < _delay) {
				yield return Singleton<CoroutineDecorator>.Instance.nop;
			}
		}
	}

	public class CoroutineDecorator {
		private Queue<int>[] _ready;
		private int _currentReadyIndex = 0;
		private Dictionary<int, CoroutineTask> _taskMap;
		private Dictionary<int, List<int> > _exitWaiting;
		private Dictionary<int, LockInfo> _lockMap;
		private int _nextLockId = 0;
		public object returnValueRegister;
		public SystemTask nop;

		private class LockInfo{
			public bool locked;
			public int? lockOwnerTid;
			public Queue<int> WaitingTid;
		}

		// Use this for initialization
		CoroutineDecorator () {
			_ready = new Queue<int> [2];
			_ready [0] = new Queue<int> ();
			_ready [1] = new Queue<int> ();
			_taskMap = new Dictionary<int, CoroutineTask> ();
			_exitWaiting = new Dictionary<int, List<int> > ();
			_lockMap = new Dictionary<int, LockInfo> ();
			nop = new SystemTask ();
			returnValueRegister = null;
		}

		public CoroutineTask getCoroutineTask(int taskId)
		{
			if (_taskMap.ContainsKey (taskId))
				return _taskMap [taskId];
			else
				return null;
		}

		public int New(IEnumerator target)
		{
			CoroutineTask newTask = new CoroutineTask (target);
			_taskMap [newTask.taskId] = newTask;
			Schedule (newTask.taskId);
			return newTask.taskId;
		}

		public int GenerateLock()
		{
			_nextLockId++;
			LockInfo alock = new LockInfo();
			alock.locked = false;
			alock.lockOwnerTid = null;
			alock.WaitingTid = new Queue<int> ();
			_lockMap.Add (_nextLockId, alock);
			return _nextLockId;
		}

		public bool AquireLock(int lockId, int taskId)
		{
			if (!_lockMap.ContainsKey (lockId))
				return true;
			LockInfo alock = _lockMap [lockId];
			if (alock.locked) {
				alock.WaitingTid.Enqueue(taskId);
				return false;
			} else {
				alock.locked = true;
				alock.lockOwnerTid = (int?)taskId;
				return true;
			}
		}

		public void ReleaseLock(int lockId)
		{
			if (!_lockMap.ContainsKey (lockId))
				return;
			LockInfo alock = _lockMap [lockId];
			if (alock.locked) {
				alock.locked = false;
				alock.lockOwnerTid = null;
				if(alock.WaitingTid.Count > 0)
				{
					int waitingTaskId = alock.WaitingTid.Dequeue();
					Schedule(waitingTaskId);
				}
			}
		}

		public bool TryLock(int lockId)
		{
			if (_lockMap.ContainsKey (lockId) == false)
				return true;
			else {
				LockInfo alock = _lockMap[lockId];
				return alock.locked;
			}
		}

		public void Schedule(int taskid)
		{
			_ready[1-_currentReadyIndex].Enqueue (taskid);
		}

		public void Exit(CoroutineTask task)
		{
			_taskMap.Remove (task.taskId);
			if (_exitWaiting.ContainsKey (task.taskId)) {
				List<int> pendingTasks = _exitWaiting [task.taskId];
				_exitWaiting.Remove (task.taskId);
				foreach(int ataskid in pendingTasks)
				{
					CoroutineTask atask = _taskMap[ataskid];
					Schedule(atask.taskId);
				}
			}

		}

		public void Callback(int taskId, object result)
		{
			CoroutineTask task = _taskMap[taskId];
			task.sendval = result;
			Schedule (task.taskId);
		}

		public bool WaitForExit(CoroutineTask task, int waitId)
		{
			if (_taskMap.ContainsKey (waitId)) {
				if(!_exitWaiting.ContainsKey(waitId))
					_exitWaiting[waitId] = new List<int>();
				_exitWaiting[waitId].Add(task.taskId);
				return true;
			} else
				return false;
		}
		
		// Update is called once per frame
		public void OnUpdate () {

			while(_ready[_currentReadyIndex].Count > 0)
			{
				int nextTaskId =  _ready[_currentReadyIndex].Dequeue();
				CoroutineTask task = null;
				if(_taskMap.ContainsKey(nextTaskId))
				{
					task = _taskMap[nextTaskId];
				}
				else
				{
					continue;
				}
				try{
					SystemTask result = task.Run();
					if(result != null)
					{
						result.task = task;
						result.Handle();
						continue;
					}
				}
				catch (StopIteration e)
				{
					Exit(task);
					continue;
				}
				Schedule(task.taskId);
			}
			_currentReadyIndex = 1 - _currentReadyIndex;
		}
	}
}