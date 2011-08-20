﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Vixen.IO;
using Vixen.Common;
using Vixen.Module.Property;

namespace Vixen.Sys {
	public class ChannelNode : GroupNode<OutputChannel>, IVersioned {
		// Making this static so there doesn't have to be potentially thousands of
		// subscriptions from the node manager.
		static public event EventHandler Changed;
		static private Dictionary<Guid, ChannelNode> _instances = new Dictionary<Guid, ChannelNode>();

		private const int VERSION = 1;

		#region Constructors
		public ChannelNode(Guid id, string name, OutputChannel channel, IEnumerable<ChannelNode> content)
			: base(name, content) {
			if (_instances.ContainsKey(id)) {
				throw new InvalidOperationException("Trying to create a ChannelNode that already exists.");
			} else {
				_instances[id] = this;
			}
			Id = id;
			Channel = channel;
			Properties = new PropertyManager(this);
		}

		public ChannelNode(string name, OutputChannel channel, IEnumerable<ChannelNode> content)
			: this(Guid.NewGuid(), name, channel, content) {
		}

		public ChannelNode(string name, IEnumerable<ChannelNode> content)
			: this(name, null, content) {
		}

		private ChannelNode(Guid id, string name, OutputChannel channel, params ChannelNode[] content)
			: this(id, name, channel, content as IEnumerable<ChannelNode>) {
		}

		public ChannelNode(string name, OutputChannel channel, params ChannelNode[] content)
			: this(name, channel, content as IEnumerable<ChannelNode>) {
		}

		public ChannelNode(string name, params ChannelNode[] content)
			: this(name, null, content) {
		}
		#endregion

		public OutputChannel Channel { get; set; }

		public Guid Id { get; private set; }

		new public ChannelNode Find(string childName) {
			return base.Find(childName) as ChannelNode;
		}

		new public IEnumerable<ChannelNode> Children {
			get { return base.Children.Cast<ChannelNode>(); }
		}

		new public IEnumerable<ChannelNode> Parents {
			get { return base.Parents.Cast<ChannelNode>(); }
		}

		public bool Masked
		{
			get { return this.All(x => x.Masked); }
			set {
				foreach(OutputChannel channel in this) {
					channel.Masked = value;
				}
			}
		}

		public ChannelNode Clone() {
			ChannelNode node = null;

			if(IsLeaf) {
				// We're cloning a node, not the channel.
				// Multiple nodes referencing a channel need to reference that same channel instance.
				node = new ChannelNode(Guid.NewGuid(), Name, this.Channel);
			} else {
				node = new ChannelNode(Guid.NewGuid(), Name, this.Channel, this.Children.Select(x => x.Clone()));
			}

			// Property data.
			node.Properties.PropertyData.Clone(this.Properties.PropertyData);

			// Properties
			foreach(IPropertyModuleInstance property in this.Properties) {
				node.Properties.Add(property.Descriptor.TypeId);
			}

			return node;
		}

		public bool IsLeaf {
			get { return base.Children.Count() == 0; }
		}

		public List<ChannelNode> InvalidChildren() {

			List<ChannelNode> result = new List<ChannelNode>();

			// the node itself is an invalid child for itself!
			result.Add(this);

			// any children it already has are invalid.
			result.AddRange(Children);

			// any parents it has (all the way back to root) are invalid,
			// otherwise that will create loops.
			result.AddRange(GetAllParentNodes());

			return result;
		}

		public PropertyManager Properties { get; private set; }

		public int Version {
			get { return VERSION; }
		}

		#region Overrides
		public override void AddChild(GroupNode<OutputChannel> node) {
			base.AddChild(node);
			OnChanged(this);
		}

		public override bool RemoveFromParent(GroupNode<OutputChannel> parent) {
			bool result = base.RemoveFromParent(parent);
			OnChanged(this);
			return result;
		}

		public override bool RemoveChild(GroupNode<OutputChannel> node) {
			bool result = base.RemoveChild(node);
			OnChanged(this);
			return result;
		}

		public override GroupNode<OutputChannel> Get(int index) {
			if(IsLeaf) throw new InvalidOperationException("Cannot get child nodes from a leaf.");
			return base.Get(index);
		}

		public override IEnumerator<OutputChannel> GetEnumerator() {
			return GetChannelEnumerator().GetEnumerator();
		}
		#endregion

		#region Enumerators
		public IEnumerable<OutputChannel> GetChannelEnumerator() {
			if(IsLeaf) {
				// OutputChannel is already an enumerable, so AsEnumerable<> won't work.
				return (new[] { Channel });
			} else {
				return this.Children.SelectMany(x => x.GetChannelEnumerator());
			}
		}

		public IEnumerable<ChannelNode> GetNodeEnumerator() {
			// "this" is already an enumerable, so AsEnumerable<> won't work.
			return (new[] { this }).Concat(Children.SelectMany(x => x.GetNodeEnumerator()));
		}

		public IEnumerable<ChannelNode> GetLeafEnumerator() {
			if(IsLeaf) {
				// OutputChannel is already an enumerable, so AsEnumerable<> won't work.
				return (new[] { this });
			} else {
				return Children.SelectMany(x => x.GetLeafEnumerator());
			}
		}

		public IEnumerable<ChannelNode> GetNonLeafEnumerator() {
			if (IsLeaf) {
				return Enumerable.Empty<ChannelNode>();
			} else {
				// "this" is already an enumerable, so AsEnumerable<> won't work.
				return (new[] { this }).Concat(Children.SelectMany(x => x.GetNonLeafEnumerator()));
			}
		}

		public IEnumerable<ChannelNode> GetAllParentNodes() {
			return Parents.Concat(Parents.SelectMany(x => x.GetAllParentNodes()));
		}
		#endregion

		#region Static members
		static protected void OnChanged(ChannelNode value) {
			if(Changed != null) {
				Changed(value, EventArgs.Empty);
			}
		}

		static public ChannelNode GetChannelNode(Guid id) {
			if (_instances.ContainsKey(id)) {
				return _instances[id];
			} else {
				return null;
			}
		}
		#endregion
	}
}