#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Reusable vehicle diagram control with clickable panels.
    /// Supports multiple vehicle types and damage/selection modes.
    /// </summary>
    public sealed class VehicleDiagramControl : UserControl
    {
        private Canvas? _vehicleCanvas;
        private readonly Dictionary<string, ShapePath> _panelPaths = new();
        private readonly HashSet<string> _selectedPanels = new();
        private CompositeTransform? _canvasTransform;

        // Pan and zoom
        private double _zoomLevel = 1.0;
        private double _panX = 0;
        private double _panY = 0;
        private bool _isPanning = false;
        private bool _wasDragged = false;
        private Windows.Foundation.Point _lastPanPoint;
        private Windows.Foundation.Point _panStartPoint;
        private const double DragThreshold = 5.0;

        // Current vehicle type
        private string _vehicleType = "sedan";

        // Colors
        private static readonly Color UnselectedFill = Color.FromArgb(255, 60, 65, 70);
        private static readonly Color UnselectedStroke = Color.FromArgb(255, 90, 95, 100);
        private static readonly Color SelectedFill = Color.FromArgb(255, 200, 80, 60);
        private static readonly Color SelectedStroke = Color.FromArgb(255, 255, 120, 80);
        private static readonly Color HoverFill = Color.FromArgb(255, 100, 105, 110);
        private static readonly Color HoverStroke = Color.FromArgb(255, 150, 155, 160);
        private static readonly Color GlassFill = Color.FromArgb(180, 100, 140, 180);
        private static readonly Color GlassStroke = Color.FromArgb(200, 120, 160, 200);

        /// <summary>
        /// Fired when a panel selection changes
        /// </summary>
        public event EventHandler<PanelSelectionChangedEventArgs>? PanelSelectionChanged;

        /// <summary>
        /// Get currently selected panel IDs
        /// </summary>
        public IReadOnlyCollection<string> SelectedPanels => _selectedPanels;

        /// <summary>
        /// Get panel display names for selected panels
        /// </summary>
        public IEnumerable<string> SelectedPanelNames => _selectedPanels
            .Select(id => GetPanelDisplayName(id))
            .Where(name => !string.IsNullOrEmpty(name));

        public VehicleDiagramControl()
        {
            BuildUI();
            DrawVehicle("sedan");
        }

        private void BuildUI()
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 28, 32)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 55, 60)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();

            // Canvas for vehicle
            _vehicleCanvas = new Canvas
            {
                Width = 300,
                Height = 400,
                Background = new SolidColorBrush(Colors.Transparent)
            };

            _canvasTransform = new CompositeTransform
            {
                ScaleX = 1,
                ScaleY = 1,
                TranslateX = 0,
                TranslateY = 0
            };
            _vehicleCanvas.RenderTransform = _canvasTransform;
            _vehicleCanvas.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

            // Pan/zoom handlers
            _vehicleCanvas.PointerPressed += Canvas_PointerPressed;
            _vehicleCanvas.PointerMoved += Canvas_PointerMoved;
            _vehicleCanvas.PointerReleased += Canvas_PointerReleased;
            _vehicleCanvas.PointerWheelChanged += Canvas_PointerWheelChanged;

            grid.Children.Add(_vehicleCanvas);

            container.Child = grid;
            Content = container;
        }

        #region Public Methods

        /// <summary>
        /// Set the vehicle type and redraw
        /// </summary>
        public void SetVehicleType(string vehicleType)
        {
            _vehicleType = vehicleType.ToLowerInvariant();
            _selectedPanels.Clear();
            DrawVehicle(_vehicleType);
            PanelSelectionChanged?.Invoke(this, new PanelSelectionChangedEventArgs(_selectedPanels.ToList()));
        }

        /// <summary>
        /// Clear all selections
        /// </summary>
        public void ClearSelections()
        {
            _selectedPanels.Clear();
            UpdateAllPanelVisuals();
            PanelSelectionChanged?.Invoke(this, new PanelSelectionChangedEventArgs(_selectedPanels.ToList()));
        }

        /// <summary>
        /// Select panels by IDs
        /// </summary>
        public void SelectPanels(IEnumerable<string> panelIds)
        {
            foreach (var id in panelIds)
            {
                _selectedPanels.Add(id);
            }
            UpdateAllPanelVisuals();
            PanelSelectionChanged?.Invoke(this, new PanelSelectionChangedEventArgs(_selectedPanels.ToList()));
        }

        /// <summary>
        /// Get panel info for all available panels
        /// </summary>
        public List<VehiclePanelInfo> GetAllPanels()
        {
            return _panelPaths.Select(kvp => new VehiclePanelInfo
            {
                Id = kvp.Key,
                DisplayName = GetPanelDisplayName(kvp.Key),
                IsSelected = _selectedPanels.Contains(kvp.Key)
            }).ToList();
        }

        #endregion

        #region Drawing Methods

        private void DrawVehicle(string vehicleType)
        {
            if (_vehicleCanvas == null) return;

            _vehicleCanvas.Children.Clear();
            _panelPaths.Clear();

            // Center point
            double cx = 150, cy = 200;

            switch (vehicleType.ToLowerInvariant())
            {
                case "coupe":
                    DrawCoupe(cx, cy);
                    break;
                case "suv":
                    DrawSUV(cx, cy);
                    break;
                case "truck":
                    DrawTruck(cx, cy);
                    break;
                case "van":
                    DrawVan(cx, cy);
                    break;
                default:
                    DrawSedan(cx, cy);
                    break;
            }

            // Add label
            AddVehicleLabel(vehicleType.ToUpper(), cx, 380);
        }

        private void DrawSedan(double cx, double cy)
        {
            // Body outline (non-selectable background)
            AddPanel("body", CreateRoundedRect(cx - 55, cy - 110, 110, 220, 30), false, "Body");

            // Hood
            AddPanel("hood", CreateRoundedRect(cx - 50, cy - 105, 100, 55, 25), true, "Hood");

            // Front bumper
            AddPanel("front_bumper", CreateRect(cx - 52, cy - 108, 104, 12), true, "Front Bumper");

            // Front fenders
            AddPanel("lf_fender", CreateRect(cx - 60, cy - 85, 12, 40), true, "LF Fender");
            AddPanel("rf_fender", CreateRect(cx + 48, cy - 85, 12, 40), true, "RF Fender");

            // Windshield (glass)
            AddGlass("windshield", CreateTrapezoid(cx - 42, cy - 48, 84, 35, 8));

            // Roof
            AddPanel("roof", CreateRoundedRect(cx - 45, cy - 15, 90, 50, 5), true, "Roof");

            // Front doors
            AddPanel("lf_door", CreateRect(cx - 55, cy - 40, 10, 55), true, "LF Door");
            AddPanel("rf_door", CreateRect(cx + 45, cy - 40, 10, 55), true, "RF Door");

            // Rear doors
            AddPanel("lr_door", CreateRect(cx - 55, cy + 15, 10, 55), true, "LR Door");
            AddPanel("rr_door", CreateRect(cx + 45, cy + 15, 10, 55), true, "RR Door");

            // Mirrors
            AddPanel("lf_mirror", CreateEllipse(cx - 62, cy - 45, 8, 12), true, "LF Mirror");
            AddPanel("rf_mirror", CreateEllipse(cx + 54, cy - 45, 8, 12), true, "RF Mirror");

            // Rear glass
            AddGlass("rear_glass", CreateTrapezoid(cx - 40, cy + 35, 80, 28, 8));

            // Quarter panels
            AddPanel("lr_quarter", CreateRect(cx - 55, cy + 65, 10, 30), true, "LR Quarter");
            AddPanel("rr_quarter", CreateRect(cx + 45, cy + 65, 10, 30), true, "RR Quarter");

            // Trunk/Decklid
            AddPanel("decklid", CreateRoundedRect(cx - 45, cy + 65, 90, 35, 15), true, "Decklid");

            // Rear bumper
            AddPanel("rear_bumper", CreateRect(cx - 52, cy + 96, 104, 12), true, "Rear Bumper");

            // Rocker panels
            AddPanel("l_rocker", CreateRect(cx - 58, cy - 35, 5, 90), true, "L Rocker");
            AddPanel("r_rocker", CreateRect(cx + 53, cy - 35, 5, 90), true, "R Rocker");

            // Headlights
            AddPanel("lf_headlight", CreateEllipse(cx - 40, cy - 100, 15, 8), true, "LF Headlight");
            AddPanel("rf_headlight", CreateEllipse(cx + 25, cy - 100, 15, 8), true, "RF Headlight");

            // Taillights
            AddPanel("lr_taillight", CreateEllipse(cx - 40, cy + 100, 15, 8), true, "LR Taillight");
            AddPanel("rr_taillight", CreateEllipse(cx + 25, cy + 100, 15, 8), true, "RR Taillight");
        }

        private void DrawCoupe(double cx, double cy)
        {
            AddPanel("body", CreateRoundedRect(cx - 55, cy - 95, 110, 190, 35), false, "Body");
            AddPanel("hood", CreateRoundedRect(cx - 50, cy - 90, 100, 65, 30), true, "Hood");
            AddPanel("front_bumper", CreateRect(cx - 52, cy - 93, 104, 12), true, "Front Bumper");
            AddPanel("lf_fender", CreateRect(cx - 62, cy - 75, 14, 45), true, "LF Fender");
            AddPanel("rf_fender", CreateRect(cx + 48, cy - 75, 14, 45), true, "RF Fender");
            AddGlass("windshield", CreateTrapezoid(cx - 40, cy - 22, 80, 30, 10));
            AddPanel("roof", CreateRoundedRect(cx - 40, cy + 8, 80, 35, 8), true, "Roof");
            AddPanel("l_door", CreateRect(cx - 55, cy - 20, 12, 60), true, "L Door");
            AddPanel("r_door", CreateRect(cx + 43, cy - 20, 12, 60), true, "R Door");
            AddPanel("lf_mirror", CreateEllipse(cx - 62, cy - 18, 8, 12), true, "LF Mirror");
            AddPanel("rf_mirror", CreateEllipse(cx + 54, cy - 18, 8, 12), true, "RF Mirror");
            AddGlass("rear_glass", CreateTrapezoid(cx - 35, cy + 43, 70, 22, 8));
            AddPanel("lr_quarter", CreateRect(cx - 53, cy + 42, 10, 35), true, "LR Quarter");
            AddPanel("rr_quarter", CreateRect(cx + 43, cy + 42, 10, 35), true, "RR Quarter");
            AddPanel("decklid", CreateRoundedRect(cx - 42, cy + 68, 84, 25, 12), true, "Decklid");
            AddPanel("rear_bumper", CreateRect(cx - 52, cy + 83, 104, 12), true, "Rear Bumper");
            AddPanel("l_rocker", CreateRect(cx - 58, cy - 15, 5, 70), true, "L Rocker");
            AddPanel("r_rocker", CreateRect(cx + 53, cy - 15, 5, 70), true, "R Rocker");
            AddPanel("lf_headlight", CreateEllipse(cx - 38, cy - 85, 16, 9), true, "LF Headlight");
            AddPanel("rf_headlight", CreateEllipse(cx + 22, cy - 85, 16, 9), true, "RF Headlight");
            AddPanel("lr_taillight", CreateEllipse(cx - 38, cy + 88, 16, 9), true, "LR Taillight");
            AddPanel("rr_taillight", CreateEllipse(cx + 22, cy + 88, 16, 9), true, "RR Taillight");
        }

        private void DrawSUV(double cx, double cy)
        {
            AddPanel("body", CreateRoundedRect(cx - 60, cy - 115, 120, 230, 20), false, "Body");
            AddPanel("hood", CreateRoundedRect(cx - 55, cy - 110, 110, 50, 15), true, "Hood");
            AddPanel("front_bumper", CreateRect(cx - 58, cy - 113, 116, 14), true, "Front Bumper");
            AddPanel("lf_fender", CreateRect(cx - 65, cy - 90, 12, 45), true, "LF Fender");
            AddPanel("rf_fender", CreateRect(cx + 53, cy - 90, 12, 45), true, "RF Fender");
            AddGlass("windshield", CreateTrapezoid(cx - 50, cy - 55, 100, 35, 5));
            AddPanel("roof", CreateRoundedRect(cx - 52, cy - 22, 104, 65, 5), true, "Roof");
            AddPanel("lf_door", CreateRect(cx - 60, cy - 50, 10, 55), true, "LF Door");
            AddPanel("rf_door", CreateRect(cx + 50, cy - 50, 10, 55), true, "RF Door");
            AddPanel("lr_door", CreateRect(cx - 60, cy + 8, 10, 55), true, "LR Door");
            AddPanel("rr_door", CreateRect(cx + 50, cy + 8, 10, 55), true, "RR Door");
            AddPanel("lf_mirror", CreateEllipse(cx - 68, cy - 52, 9, 14), true, "LF Mirror");
            AddPanel("rf_mirror", CreateEllipse(cx + 59, cy - 52, 9, 14), true, "RF Mirror");
            AddGlass("rear_glass", CreateTrapezoid(cx - 48, cy + 45, 96, 28, 5));
            AddPanel("lr_quarter", CreateRect(cx - 60, cy + 65, 10, 40), true, "LR Quarter");
            AddPanel("rr_quarter", CreateRect(cx + 50, cy + 65, 10, 40), true, "RR Quarter");
            AddPanel("liftgate", CreateRoundedRect(cx - 50, cy + 75, 100, 35, 10), true, "Liftgate");
            AddPanel("rear_bumper", CreateRect(cx - 58, cy + 103, 116, 14), true, "Rear Bumper");
            AddPanel("l_rocker", CreateRect(cx - 63, cy - 40, 5, 100), true, "L Rocker");
            AddPanel("r_rocker", CreateRect(cx + 58, cy - 40, 5, 100), true, "R Rocker");
            AddPanel("lf_headlight", CreateEllipse(cx - 45, cy - 105, 18, 10), true, "LF Headlight");
            AddPanel("rf_headlight", CreateEllipse(cx + 27, cy - 105, 18, 10), true, "RF Headlight");
            AddPanel("lr_taillight", CreateEllipse(cx - 45, cy + 107, 18, 10), true, "LR Taillight");
            AddPanel("rr_taillight", CreateEllipse(cx + 27, cy + 107, 18, 10), true, "RR Taillight");
        }

        private void DrawTruck(double cx, double cy)
        {
            // Cab
            AddPanel("cab", CreateRoundedRect(cx - 55, cy - 115, 110, 120, 15), false, "Cab");
            // Bed
            AddPanel("bed", CreateRect(cx - 50, cy + 10, 100, 95), false, "Bed");

            AddPanel("hood", CreateRoundedRect(cx - 50, cy - 110, 100, 50, 12), true, "Hood");
            AddPanel("front_bumper", CreateRect(cx - 53, cy - 113, 106, 14), true, "Front Bumper");
            AddPanel("lf_fender", CreateRect(cx - 60, cy - 95, 12, 40), true, "LF Fender");
            AddPanel("rf_fender", CreateRect(cx + 48, cy - 95, 12, 40), true, "RF Fender");
            AddGlass("windshield", CreateTrapezoid(cx - 45, cy - 58, 90, 32, 5));
            AddPanel("roof", CreateRoundedRect(cx - 45, cy - 27, 90, 35, 5), true, "Roof");
            AddPanel("lf_door", CreateRect(cx - 55, cy - 50, 10, 45), true, "LF Door");
            AddPanel("rf_door", CreateRect(cx + 45, cy - 50, 10, 45), true, "RF Door");
            AddPanel("lr_door", CreateRect(cx - 55, cy - 5, 10, 35), true, "LR Door");
            AddPanel("rr_door", CreateRect(cx + 45, cy - 5, 10, 35), true, "RR Door");
            AddPanel("lf_mirror", CreateRect(cx - 68, cy - 52, 12, 18), true, "LF Mirror");
            AddPanel("rf_mirror", CreateRect(cx + 56, cy - 52, 12, 18), true, "RF Mirror");
            AddGlass("rear_glass", CreateRect(cx - 42, cy - 25, 84, 20));
            AddPanel("l_bedside", CreateRect(cx - 55, cy + 15, 8, 85), true, "L Bedside");
            AddPanel("r_bedside", CreateRect(cx + 47, cy + 15, 8, 85), true, "R Bedside");
            AddPanel("tailgate", CreateRect(cx - 47, cy + 95, 94, 12), true, "Tailgate");
            AddPanel("rear_bumper", CreateRect(cx - 53, cy + 100, 106, 14), true, "Rear Bumper");
            AddPanel("l_rocker", CreateRect(cx - 58, cy - 45, 5, 75), true, "L Rocker");
            AddPanel("r_rocker", CreateRect(cx + 53, cy - 45, 5, 75), true, "R Rocker");
            AddPanel("lf_headlight", CreateEllipse(cx - 42, cy - 105, 18, 10), true, "LF Headlight");
            AddPanel("rf_headlight", CreateEllipse(cx + 24, cy - 105, 18, 10), true, "RF Headlight");
            AddPanel("lr_taillight", CreateEllipse(cx - 42, cy + 103, 18, 10), true, "LR Taillight");
            AddPanel("rr_taillight", CreateEllipse(cx + 24, cy + 103, 18, 10), true, "RR Taillight");
        }

        private void DrawVan(double cx, double cy)
        {
            AddPanel("body", CreateRoundedRect(cx - 60, cy - 115, 120, 230, 15), false, "Body");
            AddPanel("hood", CreateRoundedRect(cx - 55, cy - 110, 110, 35, 10), true, "Hood");
            AddPanel("front_bumper", CreateRect(cx - 58, cy - 113, 116, 14), true, "Front Bumper");
            AddPanel("lf_fender", CreateRect(cx - 63, cy - 100, 10, 30), true, "LF Fender");
            AddPanel("rf_fender", CreateRect(cx + 53, cy - 100, 10, 30), true, "RF Fender");
            AddGlass("windshield", CreateTrapezoid(cx - 52, cy - 72, 104, 35, 5));
            AddPanel("roof", CreateRoundedRect(cx - 55, cy - 40, 110, 120, 5), true, "Roof");
            AddPanel("lf_door", CreateRect(cx - 60, cy - 65, 8, 55), true, "LF Door");
            AddPanel("rf_door", CreateRect(cx + 52, cy - 65, 8, 55), true, "RF Door");
            AddPanel("sliding_door", CreateRect(cx - 60, cy - 5, 8, 70), true, "Sliding Door");
            AddPanel("r_side", CreateRect(cx + 52, cy - 5, 8, 70), true, "R Side");
            AddPanel("lf_mirror", CreateRect(cx - 70, cy - 68, 10, 16), true, "LF Mirror");
            AddPanel("rf_mirror", CreateRect(cx + 60, cy - 68, 10, 16), true, "RF Mirror");
            AddGlass("rear_glass", CreateRect(cx - 50, cy + 70, 100, 20));
            AddPanel("lr_quarter", CreateRect(cx - 60, cy + 68, 8, 35), true, "LR Quarter");
            AddPanel("rr_quarter", CreateRect(cx + 52, cy + 68, 8, 35), true, "RR Quarter");
            AddPanel("liftgate", CreateRoundedRect(cx - 50, cy + 85, 100, 25, 8), true, "Liftgate");
            AddPanel("rear_bumper", CreateRect(cx - 58, cy + 103, 116, 14), true, "Rear Bumper");
            AddPanel("lf_headlight", CreateEllipse(cx - 48, cy - 105, 16, 10), true, "LF Headlight");
            AddPanel("rf_headlight", CreateEllipse(cx + 32, cy - 105, 16, 10), true, "RF Headlight");
            AddPanel("lr_taillight", CreateEllipse(cx - 48, cy + 107, 16, 10), true, "LR Taillight");
            AddPanel("rr_taillight", CreateEllipse(cx + 32, cy + 107, 16, 10), true, "RR Taillight");
        }

        private void AddPanel(string id, Geometry geometry, bool isSelectable, string tooltip)
        {
            if (_vehicleCanvas == null) return;

            var path = new ShapePath
            {
                Data = geometry,
                Fill = new SolidColorBrush(isSelectable ? UnselectedFill : Color.FromArgb(255, 45, 48, 52)),
                Stroke = new SolidColorBrush(isSelectable ? UnselectedStroke : Color.FromArgb(255, 65, 68, 72)),
                StrokeThickness = isSelectable ? 1 : 0.5,
                Tag = id
            };

            if (isSelectable)
            {
                ToolTipService.SetToolTip(path, $"{tooltip} - Click to select");
                _panelPaths[id] = path;

                path.PointerEntered += (s, e) => OnPanelHover(path, id, true);
                path.PointerExited += (s, e) => OnPanelHover(path, id, false);
                path.PointerReleased += (s, e) => OnPanelClick(path, id, e);
            }

            _vehicleCanvas.Children.Add(path);
        }

        private void AddGlass(string id, Geometry geometry)
        {
            if (_vehicleCanvas == null) return;

            var path = new ShapePath
            {
                Data = geometry,
                Fill = new SolidColorBrush(GlassFill),
                Stroke = new SolidColorBrush(GlassStroke),
                StrokeThickness = 1,
                Tag = id
            };

            ToolTipService.SetToolTip(path, id.Replace("_", " ").ToUpper());
            _vehicleCanvas.Children.Add(path);
        }

        private void AddVehicleLabel(string text, double cx, double y)
        {
            if (_vehicleCanvas == null) return;

            var label = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 125, 130))
            };

            Canvas.SetLeft(label, cx - 25);
            Canvas.SetTop(label, y);
            _vehicleCanvas.Children.Add(label);
        }

        #endregion

        #region Geometry Helpers

        private Geometry CreateRoundedRect(double x, double y, double width, double height, double radius)
        {
            return new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(x, y, width, height)
            };
        }

        private Geometry CreateRect(double x, double y, double width, double height)
        {
            return new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(x, y, width, height)
            };
        }

        private Geometry CreateEllipse(double x, double y, double width, double height)
        {
            return new EllipseGeometry
            {
                Center = new Windows.Foundation.Point(x + width / 2, y + height / 2),
                RadiusX = width / 2,
                RadiusY = height / 2
            };
        }

        private Geometry CreateTrapezoid(double x, double y, double width, double height, double inset)
        {
            var figure = new PathFigure
            {
                StartPoint = new Windows.Foundation.Point(x + inset, y),
                IsClosed = true
            };
            figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x + width - inset, y) });
            figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x + width, y + height) });
            figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x, y + height) });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        #endregion

        #region Interaction Handlers

        private void OnPanelHover(ShapePath path, string panelId, bool isEntering)
        {
            bool isSelected = _selectedPanels.Contains(panelId);

            if (isEntering)
            {
                if (isSelected)
                {
                    path.Fill = new SolidColorBrush(Color.FromArgb(255, 230, 100, 80));
                    path.Stroke = new SolidColorBrush(Color.FromArgb(255, 255, 140, 100));
                }
                else
                {
                    path.Fill = new SolidColorBrush(HoverFill);
                    path.Stroke = new SolidColorBrush(HoverStroke);
                }
                path.StrokeThickness = 2;
            }
            else
            {
                UpdatePanelVisual(path, panelId);
            }
        }

        private void OnPanelClick(ShapePath path, string panelId, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_wasDragged) return;

            // Toggle selection
            if (_selectedPanels.Contains(panelId))
            {
                _selectedPanels.Remove(panelId);
            }
            else
            {
                _selectedPanels.Add(panelId);
            }

            UpdatePanelVisual(path, panelId);
            PanelSelectionChanged?.Invoke(this, new PanelSelectionChangedEventArgs(_selectedPanels.ToList()));
        }

        private void UpdatePanelVisual(ShapePath path, string panelId)
        {
            bool isSelected = _selectedPanels.Contains(panelId);

            if (isSelected)
            {
                path.Fill = new SolidColorBrush(SelectedFill);
                path.Stroke = new SolidColorBrush(SelectedStroke);
                path.StrokeThickness = 2;
            }
            else
            {
                path.Fill = new SolidColorBrush(UnselectedFill);
                path.Stroke = new SolidColorBrush(UnselectedStroke);
                path.StrokeThickness = 1;
            }
        }

        private void UpdateAllPanelVisuals()
        {
            foreach (var kvp in _panelPaths)
            {
                UpdatePanelVisual(kvp.Value, kvp.Key);
            }
        }

        #endregion

        #region Pan/Zoom

        private void Canvas_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_vehicleCanvas == null) return;

            var point = e.GetCurrentPoint(_vehicleCanvas);
            if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed)
            {
                _isPanning = true;
                _wasDragged = false;
                _panStartPoint = point.Position;
                _lastPanPoint = point.Position;
                _vehicleCanvas.CapturePointer(e.Pointer);
            }
        }

        private void Canvas_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isPanning || _vehicleCanvas == null || _canvasTransform == null) return;

            var point = e.GetCurrentPoint(_vehicleCanvas);
            var delta = new Windows.Foundation.Point(
                point.Position.X - _lastPanPoint.X,
                point.Position.Y - _lastPanPoint.Y
            );

            var totalDelta = Math.Sqrt(
                Math.Pow(point.Position.X - _panStartPoint.X, 2) +
                Math.Pow(point.Position.Y - _panStartPoint.Y, 2)
            );

            if (totalDelta > DragThreshold)
            {
                _wasDragged = true;
            }

            _panX += delta.X;
            _panY += delta.Y;

            _canvasTransform.TranslateX = _panX;
            _canvasTransform.TranslateY = _panY;

            _lastPanPoint = point.Position;
        }

        private void Canvas_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isPanning && _vehicleCanvas != null)
            {
                _vehicleCanvas.ReleasePointerCapture(e.Pointer);
            }
            _isPanning = false;

            // Reset drag flag after a short delay to allow click processing
            DispatcherQueue.TryEnqueue(() => _wasDragged = false);
        }

        private void Canvas_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_canvasTransform == null) return;

            var delta = e.GetCurrentPoint(_vehicleCanvas).Properties.MouseWheelDelta;
            var zoomDelta = delta > 0 ? 0.1 : -0.1;

            _zoomLevel = Math.Clamp(_zoomLevel + zoomDelta, 0.5, 2.0);

            _canvasTransform.ScaleX = _zoomLevel;
            _canvasTransform.ScaleY = _zoomLevel;
        }

        #endregion

        #region Helpers

        private string GetPanelDisplayName(string panelId)
        {
            return panelId switch
            {
                "hood" => "Hood",
                "front_bumper" => "Front Bumper",
                "rear_bumper" => "Rear Bumper",
                "lf_fender" => "LF Fender",
                "rf_fender" => "RF Fender",
                "lf_door" => "LF Door",
                "rf_door" => "RF Door",
                "lr_door" => "LR Door",
                "rr_door" => "RR Door",
                "l_door" => "L Door",
                "r_door" => "R Door",
                "lr_quarter" => "LR Quarter",
                "rr_quarter" => "RR Quarter",
                "decklid" => "Decklid",
                "liftgate" => "Liftgate",
                "tailgate" => "Tailgate",
                "roof" => "Roof",
                "l_rocker" => "L Rocker",
                "r_rocker" => "R Rocker",
                "lf_mirror" => "LF Mirror",
                "rf_mirror" => "RF Mirror",
                "lf_headlight" => "LF Headlight",
                "rf_headlight" => "RF Headlight",
                "lr_taillight" => "LR Taillight",
                "rr_taillight" => "RR Taillight",
                "l_bedside" => "L Bedside",
                "r_bedside" => "R Bedside",
                "sliding_door" => "Sliding Door",
                "r_side" => "R Side Panel",
                _ => panelId.Replace("_", " ").ToUpper()
            };
        }

        #endregion
    }

    #region Event Args and Data Classes

    public class PanelSelectionChangedEventArgs : EventArgs
    {
        public List<string> SelectedPanelIds { get; }

        public PanelSelectionChangedEventArgs(List<string> selectedPanelIds)
        {
            SelectedPanelIds = selectedPanelIds;
        }
    }

    public class VehiclePanelInfo
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsSelected { get; set; }
    }

    #endregion
}
