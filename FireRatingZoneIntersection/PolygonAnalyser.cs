﻿using System;
using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using ClipperLib;
using Path = System.Collections.Generic.List<ClipperLib.IntPoint>;
using Paths = System.Collections.Generic.List<System.Collections.Generic.List<ClipperLib.IntPoint>>;

namespace FireRatingZoneIntersection
{
    internal class PolygonAnalyser
    {
        //Nearly Entirely the Work of The Builder Coder

        /// <summary>
        /// Consider a Revit length zero
        /// if is smaller than this.
        /// </summary>
        private const double _eps = 1.0e-9;

        /// <summary>
        /// Conversion factor from feet to millimetres.
        /// </summary>
        private const double _feet_to_mm = 25.4 * 12;

        /// <summary>
        /// Conversion a given length value
        /// from feet to millimetres.
        /// </summary>
        private static long ConvertFeetToMillimetres(double d)
        {
            if (0 < d)
            {
                return _eps > d
                  ? 0
                  : (long)(_feet_to_mm * d + 0.5);
            }
            else
            {
                return _eps > -d
                  ? 0
                  : (long)(_feet_to_mm * d - 0.5);
            }
        }

        /// <summary>
        /// Conversion a given length value
        /// from millimetres to feet.
        /// </summary>
        private static double ConvertMillimetresToFeet(long d)
        {
            return d / _feet_to_mm;
        }

        /// <summary>
        /// Return a clipper integer point
        /// from a Revit model space one.
        /// Do so by dropping the Z coordinate
        /// and converting from imperial feet
        /// to millimetres.
        /// </summary>
        public IntPoint GetIntPoint(XYZ p)
        {
            return new IntPoint(ConvertFeetToMillimetres(p.X), ConvertFeetToMillimetres(p.Y));
        }

        /// <summary>
        /// Return a Revit model space point
        /// from a clipper integer one.
        /// Do so by adding a zero Z coordinate
        /// and converting from millimetres to
        /// imperial feet.
        /// </summary>
        public XYZ GetXyzPoint(IntPoint p)
        {
            return new XYZ(ConvertMillimetresToFeet(p.X), ConvertMillimetresToFeet(p.Y), 0.0);
        }

        /// <summary>
        /// Retrieve the boundary loops of the given slab
        /// top face, which is assumed to be horizontal.
        /// </summary>
        private Paths GetBoundaryLoops(CeilingAndFloor slab)
        {
            int n;
            Paths polys = null;
            Document doc = slab.Document;
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;

            Options opt = app.Create.NewGeometryOptions();

            GeometryElement geo = slab.get_Geometry(opt);

            foreach (GeometryObject obj in geo)
            {
                Solid solid = obj as Solid;
                if (null == solid)
                {
                    continue;
                }
                foreach (Face face in solid.Faces)
                {
                    PlanarFace pf = face as PlanarFace;
                    if (null != pf && pf.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ))
                    {
                        EdgeArrayArray loops = pf.EdgeLoops;

                        n = loops.Size;
                        polys = new Paths(n);

                        foreach (EdgeArray loop in loops)
                        {
                            n = loop.Size;
                            Path poly = new Path(n);

                            foreach (Edge edge in loop)
                            {
                                IList<XYZ> pts = edge.Tessellate();

                                n = pts.Count;

                                foreach (XYZ p in pts)
                                {
                                    poly.Add(GetIntPoint(p));
                                }
                            }
                            polys.Add(poly);
                        }
                    }
                }
            }
            return polys;
        }

        public List<CurveArray> Execute(Floor boundary, Floor eave)
        {
            List<CurveArray> Results = new List<CurveArray>();

            Document doc = boundary.Document;
            Application app = doc.Application;

            // Two slabs to intersect.
            CeilingAndFloor[] slab = new CeilingAndFloor[2] { eave, boundary };

            // Retrieve the two slabs' boundary loops
            Paths subj = GetBoundaryLoops(slab[0]);
            Paths clip = GetBoundaryLoops(slab[1]);

            // Calculate the intersection
            Paths intersection = new Paths();

            Clipper c = new Clipper();

            c.AddPaths(subj, PolyType.ptSubject, true);
            c.AddPaths(clip, PolyType.ptClip, true);

            c.Execute(ClipType.ctIntersection, intersection,
              PolyFillType.pftEvenOdd, PolyFillType.pftEvenOdd);

            // Check for a valid intersection
            if (0 < intersection.Count)
            {
                foreach (Path poly in intersection)
                {
                    CurveArray curves = app.Create.NewCurveArray();
                    IntPoint? p0 = null; // first
                    IntPoint? p = null; // previous

                    foreach (IntPoint q in poly)
                    {
                        if (null == p0)
                        {
                            p0 = q;
                        }
                        if (null != p)
                        {
                            curves.Append(Line.CreateBound(GetXyzPoint(p.Value), GetXyzPoint(q)));
                        }
                        p = q;
                    }
                    Results.Add(curves);
                }
            }
            return Results;
        }
    }
}